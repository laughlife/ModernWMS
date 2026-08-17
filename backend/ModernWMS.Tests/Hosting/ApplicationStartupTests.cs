using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext;
using MySqlConnector;
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
    public async Task Ordinary_web_startup_never_runs_database_initialization_even_when_legacy_setting_is_true()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "ConnectionStrings:MySqlConn",
                    "Server=127.0.0.1;Port=1;Database=must_not_be_opened;User Id=invalid;Connection Timeout=1");
                builder.UseSetting(
                    "TokenSettings:SigningKey",
                    "modernwms-local-smoke-key-32-bytes-minimum");
                builder.UseSetting("DatabaseInitialization:Enabled", "true");
            });

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Wms_and_ruoyi_contexts_share_the_single_application_database()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "ConnectionStrings:MySqlConn",
                    "Server=192.168.100.2;Port=3306;Database=ruoyi-vue-pro;User Id=ruoyi_user;Password=ruoyi_password");
                builder.UseSetting(
                    "TokenSettings:SigningKey",
                    "modernwms-local-smoke-key-32-bytes-minimum");
                builder.UseSetting("DatabaseInitialization:Enabled", "false");
            });

        using var scope = factory.Services.CreateScope();
        var wmsDbContext = scope.ServiceProvider.GetRequiredService<SqlDBContext>();
        var ruoyiDbContext = scope.ServiceProvider.GetRequiredService<RuoyiDbContext>();
        var wmsConnectionString = new DbConnectionStringBuilder
        {
            ConnectionString = wmsDbContext.Database.GetDbConnection().ConnectionString
        };
        var ruoyiConnectionString = new DbConnectionStringBuilder
        {
            ConnectionString = ruoyiDbContext.Database.GetDbConnection().ConnectionString
        };

        Assert.Equal("ruoyi-vue-pro", wmsConnectionString["database"]);
        Assert.Equal(wmsConnectionString["server"], ruoyiConnectionString["server"]);
        Assert.Equal(wmsConnectionString["database"], ruoyiConnectionString["database"]);
        Assert.Equal(wmsConnectionString["user id"], ruoyiConnectionString["user id"]);
    }

    [Fact]
    public void Dapper_factory_uses_the_same_shared_application_database()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "ConnectionStrings:MySqlConn",
                    "Server=192.168.100.2;Port=3306;Database=ruoyi-vue-pro;User Id=ruoyi_user;Password=ruoyi_password");
                builder.UseSetting(
                    "TokenSettings:SigningKey",
                    "modernwms-local-smoke-key-32-bytes-minimum");
            });

        using var scope = factory.Services.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<IMySqlConnectionFactory>();
        using var connection = connectionFactory.CreateConnection();
        var connectionString = new MySqlConnectionStringBuilder(connection.ConnectionString);

        Assert.Equal("192.168.100.2", connection.DataSource);
        Assert.Equal("ruoyi-vue-pro", connection.Database);
        Assert.Equal("ruoyi_user", connectionString.UserID);
    }

    [Fact]
    public void Every_wms_entity_maps_to_a_wms_prefixed_table()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<SqlDBContext>();

        var invalidMappings = database.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(tableName => !string.IsNullOrWhiteSpace(tableName))
            .Where(tableName => !tableName!.StartsWith("wms_", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(invalidMappings);
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
