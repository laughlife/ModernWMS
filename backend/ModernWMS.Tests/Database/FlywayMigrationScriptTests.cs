using System.Diagnostics;
using System.Text.Json;

namespace ModernWMS.Tests.Database;

public sealed class FlywayMigrationScriptTests
{
    private const string ExpectedFlywayVersion = "11.15.0";

    [Fact]
    public async Task Default_execution_only_reads_and_validates_the_schema()
    {
        var result = await RunScriptAsync();

        Assert.True(result.ExitCode == 0, $"Exit code: {result.ExitCode}\nSTDOUT: {result.StandardOutput}\nSTDERR: {result.StandardError}");
        Assert.Equal(["info", "validate"], result.Commands);
        Assert.All(result.Invocations, AssertSafeConfiguration);
    }

    [Fact]
    public async Task Apply_execution_migrates_only_after_info_and_validation()
    {
        var result = await RunScriptAsync("-Apply");

        Assert.True(result.ExitCode == 0, $"Exit code: {result.ExitCode}\nSTDOUT: {result.StandardOutput}\nSTDERR: {result.StandardError}");
        Assert.Equal(["info", "validate", "migrate"], result.Commands);
        Assert.All(result.Invocations, AssertSafeConfiguration);
    }

    [Fact]
    public async Task Execution_rejects_an_unpinned_flyway_version_before_database_commands()
    {
        var result = await RunScriptAsync(toolVersion: "11.15.1");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Invocations);
        Assert.Contains(ExpectedFlywayVersion, result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execution_requires_explicit_development_database_confirmation()
    {
        var result = await RunScriptAsync(confirmDevelopmentDatabase: false);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Invocations);
        Assert.Contains("ConfirmDevelopmentDatabase", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Execution_rejects_a_non_loopback_database_before_flyway_is_called()
    {
        var result = await RunScriptAsync(url: "jdbc:mysql://production.example.com:3306/ruoyi-vue-pro");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Invocations);
        Assert.Contains("loopback", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSafeConfiguration(IReadOnlyList<string> invocation)
    {
        Assert.Contains("-cleanDisabled=true", invocation);
        Assert.Contains("-baselineOnMigrate=false", invocation);
        Assert.Contains("-table=wms_flyway_schema_history", invocation);
        Assert.Contains(invocation, argument => argument.StartsWith("-locations=filesystem:", StringComparison.Ordinal));
        Assert.DoesNotContain(invocation, argument => argument.Contains("Password=", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ScriptResult> RunScriptAsync(
        string? switchArgument = null,
        string toolVersion = ExpectedFlywayVersion,
        bool confirmDevelopmentDatabase = true,
        string url = "jdbc:mysql://127.0.0.1:3306/test_database")
    {
        var repositoryRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "scripts", "Update-Database.ps1");
        var temporaryDirectory = Directory.CreateTempSubdirectory("modernwms-flyway-test-");
        try
        {
            var invocationLogPath = Path.Combine(temporaryDirectory.FullName, "invocations.jsonl");
            var fakeFlywayPath = Path.Combine(temporaryDirectory.FullName, "fake-flyway.ps1");
            await File.WriteAllTextAsync(fakeFlywayPath, $$"""
                if ($args -contains '-v') {
                    Write-Output 'Flyway Community Edition {{toolVersion}}'
                    return
                }
                Add-Content -LiteralPath '{{invocationLogPath.Replace("'", "''")}}' -Value ($args | ConvertTo-Json -Compress)
                return
                """);

            var arguments = new List<string>
            {
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", scriptPath,
                "-FlywayPath", fakeFlywayPath
            };
            if (confirmDevelopmentDatabase)
            {
                arguments.Add("-ConfirmDevelopmentDatabase");
            }
            if (switchArgument != null)
            {
                arguments.Add(switchArgument);
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }
            process.StartInfo.Environment["FLYWAY_URL"] = url;
            process.StartInfo.Environment["FLYWAY_USER"] = "test_user";
            process.StartInfo.Environment["FLYWAY_PASSWORD"] = "test_password";

            process.Start();
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var invocations = File.Exists(invocationLogPath)
                ? (await File.ReadAllLinesAsync(invocationLogPath))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => JsonSerializer.Deserialize<string[]>(line) ?? [])
                    .ToArray()
                : [];

            return new ScriptResult(process.ExitCode, standardOutput, standardError, invocations);
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
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

    private sealed record ScriptResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        IReadOnlyList<IReadOnlyList<string>> Invocations)
    {
        public IReadOnlyList<string> Commands => Invocations
            .Select(invocation => invocation.Last())
            .ToArray();
    }
}
