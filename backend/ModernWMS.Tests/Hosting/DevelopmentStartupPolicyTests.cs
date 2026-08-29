namespace ModernWMS.Tests.Hosting;

public class DevelopmentStartupPolicyTests
{
    [Fact]
    public void Development_launcher_never_runs_database_migrations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "一键启动前后端.ps1"));

        Assert.DoesNotContain("ApplyMigrations", script, StringComparison.Ordinal);
        Assert.DoesNotContain("initialize-database-only", script, StringComparison.Ordinal);
        Assert.DoesNotContain("DatabaseInitialization", script, StringComparison.Ordinal);
        Assert.Contains("'Watch-Backend.ps1'", script, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scripts", "一键启动前后端.ps1")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the ModernWMS repository root.");
    }
}
