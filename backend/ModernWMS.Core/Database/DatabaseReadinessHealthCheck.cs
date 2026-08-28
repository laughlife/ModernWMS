using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ModernWMS.Core.Database;

/// <summary>
/// Verifies that the application can open its configured MySQL database.
/// </summary>
public sealed class DatabaseReadinessHealthCheck(IMySqlConnectionFactory connectionFactory) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
