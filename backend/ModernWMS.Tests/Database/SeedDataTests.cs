using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.Models;
using ModernWMS.Initialization;
using ModernWMS.WMS.Entities.Models;

namespace ModernWMS.Tests.Database;

public class SeedDataTests
{
    [Fact]
    public async Task Seed_manifest_contains_legacy_minimum_data()
    {
        var manifest = await SeedManifest.LoadAsync();

        Assert.Equal(18, manifest.Menus.Count);
        Assert.Equal(18, manifest.RoleMenus.Count);
        Assert.Single(manifest.Users);
        Assert.Single(manifest.UserRoles);
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_baseline_records()
    {
        var options = new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var database = new SqlDBContext(options);
        var manifest = await SeedManifest.LoadAsync();

        await DatabaseInitializer.SeedAsync(database, manifest);
        await DatabaseInitializer.SeedAsync(database, manifest);

        Assert.Equal(18, await database.Set<MenuEntity>().CountAsync());
        Assert.Equal(18, await database.Set<RolemenuEntity>().CountAsync());
        Assert.Single(await database.Set<userEntity>().ToListAsync());
        Assert.Single(await database.Set<UserroleEntity>().ToListAsync());
    }
}
