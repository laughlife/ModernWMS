using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModernWMS.Core.DI;

namespace ModernWMS.WMS.Services.PackingTask;

/// <summary>Ruoyi 是装箱库存计划、预占、释放与消费的唯一写入方。</summary>
public interface IErpPackingStockClient : IDependency
{
    Task<ErpPackingStockResult<ErpPackingStockPlan>> GetPlanAsync(
        ErpPackingStockPlanQuery request, CancellationToken cancellationToken = default);

    Task<ErpPackingStockResult<ErpPackingStockPlan>> UpdateVariantAsync(
        ErpPackingStockVariantCommand request, CancellationToken cancellationToken = default);

    Task<ErpPackingStockResult<ErpPackingStockPlan>> UpdateContributionAsync(
        ErpPackingStockContributionCommand request, CancellationToken cancellationToken = default);

    Task<ErpPackingStockResult<ErpPackingStockPlan>> WithdrawParticipantAsync(
        ErpPackingStockParticipantWithdrawCommand request, CancellationToken cancellationToken = default);

    Task<ErpPackingStockResult<ErpPackingStockPlan>> RetryAsync(
        ErpPackingStockRetryCommand request, CancellationToken cancellationToken = default);

    Task<ErpPackingStockResult<ErpPackingStockPlan>> ConsumeAsync(
        ErpPackingStockConsumeCommand request, CancellationToken cancellationToken = default);
}

public sealed record ErpPackingStockPlanQuery(long SellfoxTaskId, long SellfoxItemId, string ActorId,
    string? ActorName = null);

public abstract record ErpPackingStockCommand(long SellfoxTaskId, long SellfoxItemId, string RequestId,
    long RowVersion, string ActorId, string ActorName)
{
    public string OperationSource { get; init; } = "WMS";
}

public sealed record ErpPackingStockVariantCommand(long SellfoxTaskId, long SellfoxItemId, string RequestId,
    long RowVersion, string ActorId, string ActorName, int Variant)
    : ErpPackingStockCommand(SellfoxTaskId, SellfoxItemId, RequestId, RowVersion, ActorId, ActorName);

public sealed record ErpPackingStockContributionCommand(long SellfoxTaskId, long SellfoxItemId, string RequestId,
    long RowVersion, string ActorId, string ActorName, long StockId, int GoodsOwnerId, long TargetQuantity,
    bool SkuMismatchConfirmed)
    : ErpPackingStockCommand(SellfoxTaskId, SellfoxItemId, RequestId, RowVersion, ActorId, ActorName);

public sealed record ErpPackingStockParticipantWithdrawCommand(long SellfoxTaskId, long SellfoxItemId,
    string RequestId, long RowVersion, string ActorId, string ActorName, int GoodsOwnerId)
    : ErpPackingStockCommand(SellfoxTaskId, SellfoxItemId, RequestId, RowVersion, ActorId, ActorName);

public sealed record ErpPackingStockRetryCommand(long SellfoxTaskId, long SellfoxItemId, string RequestId,
    long RowVersion, string ActorId, string ActorName)
    : ErpPackingStockCommand(SellfoxTaskId, SellfoxItemId, RequestId, RowVersion, ActorId, ActorName);

public sealed record ErpPackingStockConsumeCommand(long SellfoxTaskId, long SellfoxItemId, string RequestId,
    long RowVersion, string ActorId, string ActorName, IReadOnlyList<ErpPackingStockOwnerConsumption> Contributions)
    : ErpPackingStockCommand(SellfoxTaskId, SellfoxItemId, RequestId, RowVersion, ActorId, ActorName);

public sealed record ErpPackingStockOwnerConsumption(int GoodsOwnerId, long ActualPackedQty);

public sealed class ErpPackingStockPlan
{
    public string status { get; init; } = string.Empty;
    public int variant { get; init; }
    public int suggestedVariant { get; init; }
    public long requiredQty { get; init; }
    public long reservedQty { get; init; }
    public long shortageQty { get; init; }
    public bool hasSkuMismatch { get; init; }
    public long rowVersion { get; init; }
    public string? lastError { get; init; }
    public bool canUpdateVariant { get; init; }
    public bool canContribute { get; init; }
    public bool canWithdraw { get; init; }
    public bool canRetry { get; init; }
    public List<ErpPackingStockPool> pools { get; init; } = [];
    public List<ErpPackingStockParticipant> participants { get; init; } = [];
    public List<ErpPackingStockBinding> activeBindings { get; init; } = [];
}

public sealed class ErpPackingStockPool
{
    public long stockId { get; init; }
    public string skuCode { get; init; } = string.Empty;
    public int goodsOwnerId { get; init; }
    public string goodsOwnerName { get; init; } = string.Empty;
    public long ownerUserId { get; init; }
    public string ownerUserName { get; init; } = string.Empty;
    public long availableQty { get; init; }
    public long reservedQty { get; init; }
    public long contributionQty { get; init; }
    public bool skuMatched { get; init; }
    public bool canManage { get; init; }
    public long rowVersion { get; init; }
}

public sealed class ErpPackingStockParticipant
{
    public int goodsOwnerId { get; init; }
    public string goodsOwnerName { get; init; } = string.Empty;
    public long ownerUserId { get; init; }
    public string ownerUserName { get; init; } = string.Empty;
    public long contributionQty { get; init; }
    public bool canManage { get; init; }
}

public sealed class ErpPackingStockBinding
{
    public long stockId { get; init; }
    public int goodsOwnerId { get; init; }
    public long quantity { get; init; }
}

public sealed record ErpPackingStockResult<T>(bool IsSuccess, string ErrorMessage, T? Data)
{
    public static ErpPackingStockResult<T> Failure(string message) => new(false, message, default);
    public static ErpPackingStockResult<T> Success(T data) => new(true, string.Empty, data);
}

/// <summary>使用环境变量中的共享密钥调用 Ruoyi 内部库存计划接口；任意失败均不降级为本地写入。</summary>
public sealed class ErpPackingStockClient : IErpPackingStockClient
{
    private const string TokenHeader = "X-Internal-Token";
    private const string CallerHeader = "X-Internal-Caller";
    private const string Caller = "ModernWMS";
    private const string TokenEnvironment = "ERP_PACKING_STOCK_INTERNAL_TOKEN";
    private const string BasePath = "/admin-api/erp/packing-task/internal/stock-plan";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ErpPackingStockClient> _logger;

    public ErpPackingStockClient(IHttpClientFactory httpClientFactory, IConfiguration configuration,
        ILogger<ErpPackingStockClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<ErpPackingStockResult<ErpPackingStockPlan>> GetPlanAsync(ErpPackingStockPlanQuery request,
        CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Get,
        $"{BasePath}?sellfoxTaskId={request.SellfoxTaskId}&sellfoxItemId={request.SellfoxItemId}&actorId={Uri.EscapeDataString(request.ActorId)}",
        null, cancellationToken);

    public Task<ErpPackingStockResult<ErpPackingStockPlan>> UpdateVariantAsync(ErpPackingStockVariantCommand request,
        CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{BasePath}/variant", request, cancellationToken);

    public Task<ErpPackingStockResult<ErpPackingStockPlan>> UpdateContributionAsync(ErpPackingStockContributionCommand request,
        CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{BasePath}/contribution", request, cancellationToken);

    public Task<ErpPackingStockResult<ErpPackingStockPlan>> WithdrawParticipantAsync(
        ErpPackingStockParticipantWithdrawCommand request, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, $"{BasePath}/participant/withdraw", request, cancellationToken);

    public Task<ErpPackingStockResult<ErpPackingStockPlan>> RetryAsync(ErpPackingStockRetryCommand request,
        CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{BasePath}/retry", request, cancellationToken);

    public Task<ErpPackingStockResult<ErpPackingStockPlan>> ConsumeAsync(ErpPackingStockConsumeCommand request,
        CancellationToken cancellationToken = default) => SendAsync(HttpMethod.Post, $"{BasePath}/consume", request, cancellationToken);

    private async Task<ErpPackingStockResult<ErpPackingStockPlan>> SendAsync(HttpMethod method, string path,
        object? body, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["ErpIntegration:PackingStockBaseUrl"];
        var token = Environment.GetEnvironmentVariable(TokenEnvironment);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError("Ruoyi 装箱库存内部接口地址或环境密钥未配置");
            return ErpPackingStockResult<ErpPackingStockPlan>.Failure("ERP 装箱库存服务未配置，已拒绝本地写入");
        }

        try
        {
            using var request = new HttpRequestMessage(method, new Uri(baseUri, path));
            request.Headers.Add(CallerHeader, Caller);
            request.Headers.Add(TokenHeader, token);
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ruoyi 装箱库存内部接口返回 HTTP {StatusCode}", (int)response.StatusCode);
                return ErpPackingStockResult<ErpPackingStockPlan>.Failure("ERP 装箱库存服务拒绝本次操作");
            }
            var payload = await response.Content.ReadFromJsonAsync<ErpCommonResult<ErpPackingStockPlan>>(cancellationToken);
            if (payload?.code != 0 || payload.data == null)
                return ErpPackingStockResult<ErpPackingStockPlan>.Failure(payload?.msg ?? "ERP 装箱库存服务返回无效结果");
            return ErpPackingStockResult<ErpPackingStockPlan>.Success(payload.data);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(exception, "Ruoyi 装箱库存内部接口调用失败");
            return ErpPackingStockResult<ErpPackingStockPlan>.Failure("ERP 装箱库存服务不可用，已拒绝本地写入");
        }
    }

    private sealed class ErpCommonResult<T>
    {
        public int code { get; init; }
        public string? msg { get; init; }
        public T? data { get; init; }
    }
}
