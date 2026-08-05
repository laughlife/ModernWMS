using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.Models;

namespace ModernWMS.Initialization;

/// <summary>
/// Applies schema migrations and restores the deterministic baseline records.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SqlDBContext>();

        await database.Database.MigrateAsync(cancellationToken);
        var manifest = await SeedManifest.LoadAsync(cancellationToken);

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        await SeedAsync(database, manifest, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public static async Task SeedAsync(
        SqlDBContext database,
        SeedManifest manifest,
        CancellationToken cancellationToken = default)
    {
        await AddMissingAsync(database, manifest.Users, cancellationToken);
        await AddMissingAsync(database, manifest.Menus, cancellationToken);
        await AddMissingAsync(database, manifest.UserRoles, cancellationToken);
        await AddMissingAsync(database, manifest.RoleMenus, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }

    private static async Task AddMissingAsync<T>(
        SqlDBContext database,
        IReadOnlyList<T> records,
        CancellationToken cancellationToken)
        where T : BaseModel
    {
        if (records.Count == 0)
        {
            return;
        }

        var seedIds = records.Select(record => record.id).ToArray();
        var existingIds = await database.Set<T>()
            .AsNoTracking()
            .Where(record => seedIds.Contains(record.id))
            .Select(record => record.id)
            .ToListAsync(cancellationToken);
        var existingIdSet = existingIds.ToHashSet();
        var missingRecords = records.Where(record => !existingIdSet.Contains(record.id));

        await database.Set<T>().AddRangeAsync(missingRecords, cancellationToken);
    }
}
