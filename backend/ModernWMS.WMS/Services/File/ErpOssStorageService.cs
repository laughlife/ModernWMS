using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using OSS = AlibabaCloud.OSS.V2;

namespace ModernWMS.WMS.Services;

/// <summary>
/// 直接读取 ERP 的主文件配置，并通过阿里云 OSS 原生 SDK 上传图片。
/// </summary>
public class ErpOssStorageService : IErpOssStorageService
{
    private const int ErpS3Storage = 20;
    private const int ErpAliyunOssStorage = 21;
    private const long MaxImageSize = 10 * 1024 * 1024;
    private const long ShenzhenWarehouseId = 320118;
    private const string ObjectPrefix = "modernwms/erp-receipt";
    private static readonly HashSet<string> AllowedCategories = ["freight", "loss", "receipt"];
    private readonly RuoyiDbContext _ruoyiDbContext;

    public ErpOssStorageService(RuoyiDbContext ruoyiDbContext)
    {
        _ruoyiDbContext = ruoyiDbContext;
    }

    public async Task<OssFileUploadViewModel> UploadImageAsync(IFormFile file, long shipmentId, string category)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("请选择需要上传的图片");
        }
        if (file.Length > MaxImageSize)
        {
            throw new ArgumentException("单张图片不能超过 10MB");
        }
        if (shipmentId <= 0)
        {
            throw new ArgumentException("货件 ID 无效");
        }
        if (!AllowedCategories.Contains(category))
        {
            throw new ArgumentException("图片分类无效");
        }
        var shipmentExists = await _ruoyiDbContext.LogisticsInfos
            .AsNoTracking()
            .AnyAsync(t => t.id == shipmentId && !t.deleted && t.to_warehouse_id == ShenzhenWarehouseId);
        if (!shipmentExists)
        {
            throw new ArgumentException("未找到深圳自建仓对应货件");
        }

        await using var input = file.OpenReadStream();
        await using var content = new MemoryStream((int)file.Length);
        await input.CopyToAsync(content);
        var image = DetectImage(content);
        content.Position = 0;

        var objectPath = $"{ObjectPrefix}/{category}/{shipmentId}/{DateTime.UtcNow:yyyyMMdd}/{Guid.NewGuid():N}{image.extension}";
        var storage = await LoadStorageAsync();
        using var client = CreateClient(storage);
        await client.PutObjectAsync(new OSS.Models.PutObjectRequest
        {
            Bucket = storage.bucket,
            Key = objectPath,
            Body = content,
            ContentType = image.contentType
        });

        var url = BuildCanonicalUrl(storage, objectPath);
        var accessUrl = client.Presign(new OSS.Models.GetObjectRequest
        {
            Bucket = storage.bucket,
            Key = objectPath
        }).Url;

        return new OssFileUploadViewModel
        {
            name = Path.GetFileName(file.FileName),
            path = objectPath,
            url = url,
            access_url = accessUrl,
            content_type = image.contentType,
            size = file.Length
        };
    }

    public async Task<string> CreateAccessUrlAsync(string path)
    {
        var normalizedPath = path?.Trim().TrimStart('/') ?? string.Empty;
        if (!normalizedPath.StartsWith(ObjectPrefix + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException("OSS 图片路径无效");
        }

        var storage = await LoadStorageAsync();
        using var client = CreateClient(storage);
        return client.Presign(new OSS.Models.GetObjectRequest
        {
            Bucket = storage.bucket,
            Key = normalizedPath
        }).Url;
    }

    private async Task<ErpOssConfig> LoadStorageAsync()
    {
        var entity = await _ruoyiDbContext.FileConfigs
            .AsNoTracking()
            .Where(t => t.master && !t.deleted && (t.storage == ErpS3Storage || t.storage == ErpAliyunOssStorage))
            .OrderByDescending(t => t.id)
            .FirstOrDefaultAsync();
        if (entity == null)
        {
            throw new InvalidOperationException("ERP 未配置可用的主 OSS 存储");
        }

        var config = JsonSerializer.Deserialize<ErpOssConfig>(entity.config, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (config == null
            || string.IsNullOrWhiteSpace(config.endpoint)
            || string.IsNullOrWhiteSpace(config.bucket)
            || string.IsNullOrWhiteSpace(config.accessKey)
            || string.IsNullOrWhiteSpace(config.accessSecret))
        {
            throw new InvalidOperationException("ERP 主 OSS 配置不完整");
        }
        if (!config.endpoint.Contains("aliyuncs.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ERP 当前主文件配置不是阿里云 OSS");
        }

        config.endpoint = EnsureHttps(config.endpoint);
        config.region = string.IsNullOrWhiteSpace(config.region) ? ResolveRegion(config.endpoint) : config.region;
        return config;
    }

    private static OSS.Client CreateClient(ErpOssConfig storage)
    {
        var configuration = OSS.Configuration.LoadDefault();
        configuration.CredentialsProvider = new OSS.Credentials.StaticCredentialsProvider(
            storage.accessKey,
            storage.accessSecret);
        configuration.Region = storage.region;
        configuration.Endpoint = storage.endpoint;
        return new OSS.Client(configuration);
    }

    private static string BuildCanonicalUrl(ErpOssConfig storage, string path)
    {
        var domain = storage.domain?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(domain))
        {
            var endpoint = new Uri(storage.endpoint);
            domain = $"{endpoint.Scheme}://{storage.bucket}.{endpoint.Host}";
        }
        else
        {
            domain = EnsureHttps(domain);
        }
        return $"{domain}/{path}";
    }

    private static string EnsureHttps(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static string ResolveRegion(string endpoint)
    {
        var host = new Uri(endpoint).Host;
        var prefix = host.Split('.')[0];
        if (!prefix.StartsWith("oss-", StringComparison.OrdinalIgnoreCase) || prefix.Length <= 4)
        {
            throw new InvalidOperationException("无法从 ERP OSS endpoint 识别区域");
        }
        return prefix[4..];
    }

    private static (string extension, string contentType) DetectImage(MemoryStream content)
    {
        var bytes = content.ToArray();
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return (".jpg", "image/jpeg");
        }
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return (".png", "image/png");
        }
        if (bytes.Length >= 6 && (bytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8) || bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8)))
        {
            return (".gif", "image/gif");
        }
        if (bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return (".webp", "image/webp");
        }
        throw new ArgumentException("仅支持 JPG、PNG、GIF、WEBP 图片");
    }

    private sealed class ErpOssConfig
    {
        public string endpoint { get; set; } = string.Empty;
        public string? domain { get; set; }
        public string bucket { get; set; } = string.Empty;
        public string? region { get; set; }
        public string accessKey { get; set; } = string.Empty;
        public string accessSecret { get; set; } = string.Empty;
    }
}
