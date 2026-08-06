using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModernWMS.Core.DBContext;
using System.Data.Common;

namespace ModernWMS.Tests.Hosting;

public class ApplicationStartupTests
{
    [Fact]
    public async Task Health_endpoint_is_available_without_database_initialization()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Swagger_home_page_starts_with_environment_configuration()
    {
        await using var factory = CreateFactory();

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("swagger-ui", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Erp_context_uses_its_dedicated_connection_string()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "ConnectionStrings:MySqlConn",
                    "Server=127.0.0.1;Port=3306;Database=modernwms_smoke;User Id=wms_user;Password=wms_password");
                builder.UseSetting(
                    "ConnectionStrings:ErpMySqlConn",
                    "Server=192.168.100.2;Port=3306;Database=ruoyi-vue-pro;User Id=erp_user;Password=erp_password");
                builder.UseSetting(
                    "TokenSettings:SigningKey",
                    "modernwms-local-smoke-key-32-bytes-minimum");
                builder.UseSetting("DatabaseInitialization:Enabled", "false");
            });

        using var scope = factory.Services.CreateScope();
        var erpDbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var connectionString = new DbConnectionStringBuilder
        {
            ConnectionString = erpDbContext.Database.GetDbConnection().ConnectionString
        };

        Assert.Equal("192.168.100.2", connectionString["server"]);
        Assert.Equal("ruoyi-vue-pro", connectionString["database"]);
        Assert.Equal("erp_user", connectionString["user id"]);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "ConnectionStrings:MySqlConn",
                    "Server=127.0.0.1;Port=3306;Database=modernwms_smoke;User Id=smoke");
                builder.UseSetting(
                    "TokenSettings:SigningKey",
                    "modernwms-local-smoke-key-32-bytes-minimum");
                builder.UseSetting("DatabaseInitialization:Enabled", "false");
            });
}
