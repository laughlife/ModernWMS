using System.Runtime.CompilerServices;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using ModernWMS.Core.Database;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.IServices;
using OSS = AlibabaCloud.OSS.V2;

[assembly: InternalsVisibleTo("ModernWMS.Tests")]

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
    private readonly IErpOssDataSource _dataSource;
    private readonly IErpOssClientFactory _clientFactory;

    /// <summary>
    /// 初始化 ErpOssStorageService 的新实例。
    /// </summary>
    public ErpOssStorageService(IMySqlConnectionFactory connectionFactory)
        : this(new DapperErpOssDataSource(connectionFactory), AlibabaErpOssClientFactory.Instance)
    {
    }

    internal ErpOssStorageService(IErpOssDataSource dataSource, IErpOssClientFactory clientFactory)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <summary>
    /// 执行 UploadImageAsync 操作。
    /// </summary>
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
        var shipmentExists = await _dataSource.ShipmentExistsAsync(
            shipmentId,
            ShenzhenWarehouseId,
            CancellationToken.None);
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
        using var client = _clientFactory.Create(storage);
        await client.UploadAsync(objectPath, content, image.contentType, CancellationToken.None);

        var url = BuildCanonicalUrl(storage, objectPath);
        var accessUrl = client.CreateAccessUrl(objectPath);

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

    /// <summary>
    /// 执行 CreateAccessUrlAsync 操作。
    /// </summary>
    public async Task<string> CreateAccessUrlAsync(string path)
    {
        var normalizedPath = path?.Trim().TrimStart('/') ?? string.Empty;
        if (!normalizedPath.StartsWith(ObjectPrefix + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException("OSS 图片路径无效");
        }

        var storage = await LoadStorageAsync();
        using var client = _clientFactory.Create(storage);
        return client.CreateAccessUrl(normalizedPath);
    }

    private async Task<ErpOssStorageSettings> LoadStorageAsync()
    {
        var rows = await _dataSource.LoadStorageRowsAsync(CancellationToken.None);
        var entity = rows
            .Where(t => t.Master && !t.Deleted && (t.Storage == ErpS3Storage || t.Storage == ErpAliyunOssStorage))
            .OrderByDescending(t => t.Id)
            .FirstOrDefault();
        if (entity == null)
        {
            throw new InvalidOperationException("ERP 未配置可用的主 OSS 存储");
        }

        var config = JsonSerializer.Deserialize<ErpOssConfig>(entity.Config, new JsonSerializerOptions
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

        var endpoint = EnsureHttps(config.endpoint);
        var region = string.IsNullOrWhiteSpace(config.region) ? ResolveRegion(endpoint) : config.region;
        return new ErpOssStorageSettings(
            endpoint,
            config.domain,
            config.bucket,
            region,
            config.accessKey,
            config.accessSecret);
    }

    private static string BuildCanonicalUrl(ErpOssStorageSettings storage, string path)
    {
        var domain = storage.Domain?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(domain))
        {
            var endpoint = new Uri(storage.Endpoint);
            domain = $"{endpoint.Scheme}://{storage.Bucket}.{endpoint.Host}";
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

internal sealed record ErpOssStorageRow(long Id, int Storage, bool Master, bool Deleted, string Config);

internal sealed record ErpOssStorageSettings(
    string Endpoint,
    string? Domain,
    string Bucket,
    string Region,
    string AccessKey,
    string AccessSecret);

internal interface IErpOssDataSource
{
    Task<bool> ShipmentExistsAsync(long shipmentId, long warehouseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ErpOssStorageRow>> LoadStorageRowsAsync(CancellationToken cancellationToken);
}

internal sealed class DapperErpOssDataSource(IMySqlConnectionFactory connectionFactory) : IErpOssDataSource
{
    private readonly IMySqlConnectionFactory _connectionFactory =
        connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    public async Task<bool> ShipmentExistsAsync(
        long shipmentId,
        long warehouseId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM `trk_logistics_info`
                WHERE `id` = @shipment_id
                  AND `deleted` = 0
                  AND `to_warehouse_id` = @warehouse_id
            );
            """;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<bool>(new CommandDefinition(
            sql,
            new { shipment_id = shipmentId, warehouse_id = warehouseId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ErpOssStorageRow>> LoadStorageRowsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                `id` AS `Id`,
                `storage` AS `Storage`,
                `master` AS `Master`,
                `deleted` AS `Deleted`,
                `config` AS `Config`
            FROM `infra_file_config`
            WHERE `master` = 1
              AND `deleted` = 0
              AND `storage` IN (20, 21)
            ORDER BY `id` DESC;
            """;
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ErpOssStorageRow>(new CommandDefinition(
            sql,
            cancellationToken: cancellationToken));
        return rows.AsList();
    }
}

internal interface IErpOssClientFactory
{
    IErpOssClient Create(ErpOssStorageSettings storage);
}

internal interface IErpOssClient : IDisposable
{
    Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken);

    string CreateAccessUrl(string path);
}

internal sealed class AlibabaErpOssClientFactory : IErpOssClientFactory
{
    public static AlibabaErpOssClientFactory Instance { get; } = new();

    private AlibabaErpOssClientFactory()
    {
    }

    public IErpOssClient Create(ErpOssStorageSettings storage)
    {
        var configuration = OSS.Configuration.LoadDefault();
        configuration.CredentialsProvider = new OSS.Credentials.StaticCredentialsProvider(
            storage.AccessKey,
            storage.AccessSecret);
        configuration.Region = storage.Region;
        configuration.Endpoint = storage.Endpoint;
        return new AlibabaErpOssClient(new OSS.Client(configuration), storage.Bucket);
    }
}

internal sealed class AlibabaErpOssClient(OSS.Client client, string bucket) : IErpOssClient
{
    private readonly OSS.Client _client = client;

    public async Task UploadAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _client.PutObjectAsync(new OSS.Models.PutObjectRequest
        {
            Bucket = bucket,
            Key = path,
            Body = content,
            ContentType = contentType
        });
    }

    public string CreateAccessUrl(string path) => _client.Presign(new OSS.Models.GetObjectRequest
    {
        Bucket = bucket,
        Key = path
    }).Url ?? throw new InvalidOperationException("OSS 未返回可用的访问地址");

    public void Dispose() => _client.Dispose();
}
