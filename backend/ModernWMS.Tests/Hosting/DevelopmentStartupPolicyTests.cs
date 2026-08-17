namespace ModernWMS.Tests.Hosting;

public class DevelopmentStartupPolicyTests
{
    [Fact]
    public void Development_launcher_requires_an_explicit_switch_before_running_database_migrations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "Start-Development.ps1"));

        Assert.Contains("[switch]$ApplyMigrations", script, StringComparison.Ordinal);
        Assert.Contains("if ($ApplyMigrations)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'[1/3] 初始化并迁移数据库。失败时不会启动后端或前端。'", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scripts", "Start-Development.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the ModernWMS repository root.");
    }
}
