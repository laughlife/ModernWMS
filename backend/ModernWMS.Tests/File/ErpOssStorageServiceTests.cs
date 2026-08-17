using Microsoft.AspNetCore.Http;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Oss;

public class ErpOssStorageServiceTests
{
    [Fact]
    public async Task UploadImageAsync_rejects_an_unapproved_category_before_database_access()
    {
        var dataSource = new FakeDataSource { ShipmentExists = true };
        var service = new ErpOssStorageService(dataSource, new FakeClientFactory());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadImageAsync(PngFile(), 123, "receipt/../../outside"));

        Assert.Equal("图片分类无效", exception.Message);
        Assert.Equal(0, dataSource.RequestedShipmentId);
    }

    [Fact]
    public async Task CreateAccessUrlAsync_rejects_a_path_outside_the_owned_prefix_before_database_access()
    {
        var dataSource = new FakeDataSource();
        var service = new ErpOssStorageService(dataSource, new FakeClientFactory());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAccessUrlAsync("modernwms/erp-receiptx/receipt/123/a.png"));

        Assert.Equal("OSS 图片路径无效", exception.Message);
        Assert.Equal(0, dataSource.StorageLoadCount);
    }

    [Fact]
    public async Task UploadImageAsync_rejects_a_shipment_outside_the_Shenzhen_warehouse_before_OSS_access()
    {
        var dataSource = new FakeDataSource { ShipmentExists = false };
        var clients = new FakeClientFactory();
        var service = new ErpOssStorageService(dataSource, clients);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadImageAsync(PngFile(), 123, "receipt"));

        Assert.Equal("未找到深圳自建仓对应货件", exception.Message);
        Assert.Equal(123, dataSource.RequestedShipmentId);
        Assert.Equal(320118, dataSource.RequestedWarehouseId);
        Assert.Equal(0, dataSource.StorageLoadCount);
        Assert.Equal(0, clients.CreateCount);
    }

    [Fact]
    public async Task CreateAccessUrlAsync_selects_the_latest_active_primary_OSS_configuration()
    {
        var dataSource = new FakeDataSource
        {
            StorageRows =
            [
                new(40, 21, true, true, Config("deleted-bucket")),
                new(30, 21, true, false, Config("latest-bucket")),
                new(20, 20, true, false, Config("older-bucket")),
                new(10, 21, false, false, Config("not-primary"))
            ]
        };
        var clients = new FakeClientFactory();
        var service = new ErpOssStorageService(dataSource, clients);

        var url = await service.CreateAccessUrlAsync("modernwms/erp-receipt/receipt/123/a.png");

        Assert.Equal("signed://latest-bucket/modernwms/erp-receipt/receipt/123/a.png", url);
        Assert.Equal("latest-bucket", clients.LastStorage?.Bucket);
        Assert.Equal(1, clients.CreateCount);
    }

    [Fact]
    public async Task UploadImageAsync_rejects_content_that_is_not_a_supported_image_before_loading_OSS_configuration()
    {
        var dataSource = new FakeDataSource { ShipmentExists = true };
        var clients = new FakeClientFactory();
        var service = new ErpOssStorageService(dataSource, clients);
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        var file = FormFile(bytes, "fake.png");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadImageAsync(file, 123, "receipt"));

        Assert.Equal("仅支持 JPG、PNG、GIF、WEBP 图片", exception.Message);
        Assert.Equal(0, dataSource.StorageLoadCount);
        Assert.Equal(0, clients.CreateCount);
    }

    [Fact]
    public async Task UploadImageAsync_uses_detected_image_type_and_fake_OSS_client()
    {
        var dataSource = new FakeDataSource
        {
            ShipmentExists = true,
            StorageRows = [new(1, 21, true, false, Config("main-bucket"))]
        };
        var clients = new FakeClientFactory();
        var service = new ErpOssStorageService(dataSource, clients);

        var result = await service.UploadImageAsync(PngFile(), 123, "receipt");

        Assert.Equal("image/png", result.content_type);
        Assert.EndsWith(".png", result.path);
        Assert.Equal("signed://main-bucket/" + result.path, result.access_url);
        Assert.Equal(result.path, clients.Client.UploadedPath);
        Assert.Equal("image/png", clients.Client.UploadedContentType);
    }

    private static FormFile PngFile() => FormFile(
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
        "proof.jpg");

    private static FormFile FormFile(byte[] bytes, string fileName)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName);
    }

    private static string Config(string bucket) => $$"""
        {"endpoint":"oss-cn-shenzhen.aliyuncs.com","bucket":"{{bucket}}","accessKey":"key","accessSecret":"secret"}
        """;

    private sealed class FakeDataSource : IErpOssDataSource
    {
        public bool ShipmentExists { get; init; }
        public long RequestedShipmentId { get; private set; }
        public long RequestedWarehouseId { get; private set; }
        public int StorageLoadCount { get; private set; }
        public IReadOnlyList<ErpOssStorageRow> StorageRows { get; init; } = [];

        public Task<bool> ShipmentExistsAsync(long shipmentId, long warehouseId, CancellationToken cancellationToken)
        {
            RequestedShipmentId = shipmentId;
            RequestedWarehouseId = warehouseId;
            return Task.FromResult(ShipmentExists);
        }

        public Task<IReadOnlyList<ErpOssStorageRow>> LoadStorageRowsAsync(CancellationToken cancellationToken)
        {
            StorageLoadCount++;
            return Task.FromResult(StorageRows);
        }
    }

    private sealed class FakeClientFactory : IErpOssClientFactory
    {
        public int CreateCount { get; private set; }
        public ErpOssStorageSettings? LastStorage { get; private set; }
        public FakeClient Client { get; } = new();

        public IErpOssClient Create(ErpOssStorageSettings storage)
        {
            CreateCount++;
            LastStorage = storage;
            Client.Bucket = storage.Bucket;
            return Client;
        }
    }

    private sealed class FakeClient : IErpOssClient
    {
        public string Bucket { get; set; } = string.Empty;
        public string? UploadedPath { get; private set; }
        public string? UploadedContentType { get; private set; }

        public Task UploadAsync(string path, Stream content, string contentType, CancellationToken cancellationToken)
        {
            UploadedPath = path;
            UploadedContentType = contentType;
            return Task.CompletedTask;
        }

        public string CreateAccessUrl(string path) => $"signed://{Bucket}/{path}";

        public void Dispose()
        {
        }
    }
}
