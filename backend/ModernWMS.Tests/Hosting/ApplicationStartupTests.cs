using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ModernWMS.Tests.Hosting;

public class ApplicationStartupTests
{
    [Fact]
    public async Task Swagger_home_page_starts_with_environment_configuration()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting(
                    "ConnectionStrings:MySqlConn",
                    "Server=127.0.0.1;Port=3306;Database=modernwms_smoke;User Id=smoke");
                builder.UseSetting(
                    "TokenSettings:SigningKey",
                    "modernwms-local-smoke-key-32-bytes-minimum");
            });

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("swagger-ui", content, StringComparison.OrdinalIgnoreCase);
    }
}
