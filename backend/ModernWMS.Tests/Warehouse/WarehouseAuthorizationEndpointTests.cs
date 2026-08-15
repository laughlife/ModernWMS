using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ModernWMS.Tests.Warehouse;

public class WarehouseAuthorizationEndpointTests
{
    [Theory]
    [InlineData("GET", "/warehouse/access-options")]
    [InlineData("GET", "/rolemenu/warehouses?userrole_id=1")]
    [InlineData("PUT", "/rolemenu/warehouses")]
    public async Task Warehouse_security_endpoints_reject_anonymous_requests(string method, string url)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { userrole_id = 1, warehouse_ids = new[] { 320118 } });
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "ConnectionStrings:MySqlConn",
                    "Server=127.0.0.1;Port=3306;Database=ruoyi_smoke;User Id=smoke");
                builder.UseSetting(
                    "TokenSettings:SigningKey",
                    "modernwms-local-smoke-key-32-bytes-minimum");
                builder.UseSetting("DatabaseInitialization:Enabled", "false");
            });
}
