using Microsoft.Extensions.DependencyInjection;

namespace ModernWMS.Core.Database;

/// <summary>
/// Registers the shared MySQL data-access infrastructure.
/// </summary>
public static class DatabaseServiceCollectionExtensions
{
    /// <summary>
    /// Registers one shared connection-pool-backed factory.
    /// </summary>
    public static IServiceCollection AddModernWmsDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<IMySqlConnectionFactory>(
            _ => new MySqlConnectionFactory(connectionString));
        return services;
    }
}
