using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using ModernWMS.Core.DBContext;

namespace ModernWMS.Initialization;

/// <summary>
/// Creates the MySQL context used by EF tooling without starting the web host.
/// </summary>
public sealed class DesignTimeSqlDbContextFactory : IDesignTimeDbContextFactory<SqlDBContext>
{
    private const string DesignTimeConnectionString =
        "Server=127.0.0.1;Port=3306;Database=wms;User Id=modernwms_migrations;Character Set=utf8mb4";

    public SqlDBContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MySqlConn");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DesignTimeConnectionString;
        }

        var options = new DbContextOptionsBuilder<SqlDBContext>()
            .UseMySQL(connectionString, mysql => mysql.MigrationsAssembly("ModernWMS"))
            .Options;

        return new SqlDBContext(options);
    }
}
