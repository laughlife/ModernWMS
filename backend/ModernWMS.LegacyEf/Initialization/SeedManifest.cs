using System.Reflection;
using System.Text.Json;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.Models;

namespace ModernWMS.Initialization;

/// <summary>
/// The deterministic baseline records extracted from the legacy SQLite database.
/// </summary>
public sealed class SeedManifest
{
    private const string ResourcePrefix = "ModernWMS.SeedData";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private SeedManifest(
        IReadOnlyList<MenuEntity> menus,
        IReadOnlyList<RolemenuEntity> roleMenus,
        IReadOnlyList<userEntity> users,
        IReadOnlyList<UserroleEntity> userRoles)
    {
        Menus = menus;
        RoleMenus = roleMenus;
        Users = users;
        UserRoles = userRoles;
    }

    public IReadOnlyList<MenuEntity> Menus { get; }

    public IReadOnlyList<RolemenuEntity> RoleMenus { get; }

    public IReadOnlyList<userEntity> Users { get; }

    public IReadOnlyList<UserroleEntity> UserRoles { get; }

    public static async Task<SeedManifest> LoadAsync(CancellationToken cancellationToken = default)
    {
        var assembly = typeof(SeedManifest).Assembly;

        return new SeedManifest(
            await LoadResourceAsync<MenuEntity>(assembly, "menu.json", cancellationToken),
            await LoadResourceAsync<RolemenuEntity>(assembly, "rolemenu.json", cancellationToken),
            await LoadResourceAsync<userEntity>(assembly, "user.json", cancellationToken),
            await LoadResourceAsync<UserroleEntity>(assembly, "userrole.json", cancellationToken));
    }

    private static async Task<IReadOnlyList<T>> LoadResourceAsync<T>(
        Assembly assembly,
        string fileName,
        CancellationToken cancellationToken)
    {
        var resourceName = $"{ResourcePrefix}.{fileName}";
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded seed resource '{resourceName}' was not found.");

        return await JsonSerializer.DeserializeAsync<List<T>>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException($"Embedded seed resource '{resourceName}' is empty.");
    }
}
