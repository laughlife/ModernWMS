using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Warehouse;

public class WarehouseAccessServiceTests
{
    [Fact]
    public async Task GetAllowedAsync_admin_returns_only_domestic_warehouses_and_prefers_320118()
    {
        var service = CreateService();

        var result = await service.GetAllowedAsync(new CurrentUser { user_role = " Admin ", tenant_id = 99 });

        Assert.Equal([9L, 320118L], result.warehouses.Select(t => t.id).ToArray());
        Assert.Equal(320118L, result.default_warehouse_id);
    }

    [Fact]
    public async Task GetAllowedAsync_unbound_ordinary_role_returns_empty_without_default()
    {
        var service = CreateService();

        var result = await service.GetAllowedAsync(new CurrentUser { user_role = "picker", tenant_id = 1 });

        Assert.Empty(result.warehouses);
        Assert.Null(result.default_warehouse_id);
    }

    [Fact]
    public async Task GetAllowedAsync_unions_same_normalized_role_bindings_without_using_tenant_as_visibility_filter()
    {
        var service = CreateService(
            new WarehouseAccessService.RoleWarehouseBinding { role_name = "Picker", warehouse_id = 9 },
            new WarehouseAccessService.RoleWarehouseBinding { role_name = " picker ", warehouse_id = 320118 });

        var result = await service.GetAllowedAsync(new CurrentUser { user_role = " PICKER ", tenant_id = 777 });

        Assert.Equal([9L, 320118L], result.warehouses.Select(t => t.id).ToArray());
        Assert.Equal(9L, result.default_warehouse_id);
    }

    [Fact]
    public async Task EnsureAllowedAsync_rejects_a_direct_request_outside_the_role_binding()
    {
        var service = CreateService(
            new WarehouseAccessService.RoleWarehouseBinding { role_name = "picker", warehouse_id = 9 });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.EnsureAllowedAsync(320118, new CurrentUser { user_role = "picker", tenant_id = 1 }));
    }

    private static WarehouseAccessService CreateService(
        params WarehouseAccessService.RoleWarehouseBinding[] bindings)
    {
        return new WarehouseAccessService(new FakeWarehouseAccessDataSource(bindings));
    }

    private sealed class FakeWarehouseAccessDataSource(
        IEnumerable<WarehouseAccessService.RoleWarehouseBinding> bindings)
        : WarehouseAccessService.IWarehouseAccessDataSource
    {
        private readonly List<WarehouseAccessService.RoleWarehouseBinding> _bindings = bindings.ToList();

        public Task<List<ErpWarehouseOptionViewModel>> GetDomesticWarehousesAsync() => Task.FromResult(
            new List<ErpWarehouseOptionViewModel>
            {
                new() { id = 9, name = "备用仓" },
                new() { id = 320118, name = "深圳自建仓" }
            });

        public Task<List<WarehouseAccessService.RoleWarehouseBinding>> GetValidRoleBindingsAsync() =>
            Task.FromResult(_bindings);
    }
}
