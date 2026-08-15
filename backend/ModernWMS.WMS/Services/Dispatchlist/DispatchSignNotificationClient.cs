using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModernWMS.Core.DI;
using System.Net.Http.Json;

namespace ModernWMS.WMS.Services.Dispatchlist;

/// <summary>Notifies ERP after a WMS dispatch is signed.</summary>
public interface IDispatchSignNotificationClient : IDependency
{
    /// <summary>Publishes a successful WMS signing event.</summary>
    Task NotifySignedAsync(string dispatchNo, CancellationToken cancellationToken = default);

    /// <summary>Attempts delivery and reports whether the downstream endpoint accepted it.</summary>
    Task<bool> TryNotifySignedAsync(string dispatchNo, CancellationToken cancellationToken = default);
}

/// <summary>Token-authenticated ERP client for WMS signing notifications.</summary>
public sealed class DispatchSignNotificationClient : IDispatchSignNotificationClient
{
    private const string TokenHeader = "X-WMS-Internal-Token";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DispatchSignNotificationClient> _logger;

    /// <summary>Creates the ERP signing notification client.</summary>
    public DispatchSignNotificationClient(IHttpClientFactory httpClientFactory, IConfiguration configuration,
        ILogger<DispatchSignNotificationClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifySignedAsync(string dispatchNo, CancellationToken cancellationToken = default)
    {
        _ = await TryNotifySignedAsync(dispatchNo, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryNotifySignedAsync(
        string dispatchNo, CancellationToken cancellationToken = default)
    {
        var url = _configuration["ErpIntegration:WmsSignNotificationUrl"];
        var token = _configuration["ErpIntegration:InternalToken"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError("ERP 签收通知地址或内部 Token 未配置，dispatchNo={DispatchNo}", dispatchNo);
            return false;
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(new { dispatchNo })
            };
            request.Headers.Add(TokenHeader, token);
            using var response = await _httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERP 签收钉钉通知调用失败，dispatchNo={DispatchNo}", dispatchNo);
            return false;
        }
    }
}
