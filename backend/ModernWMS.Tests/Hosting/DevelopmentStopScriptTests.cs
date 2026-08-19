using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ModernWMS.Tests.Hosting;

public class DevelopmentStopScriptTests
{
    [Fact]
    public void Stop_script_matches_iso_start_time_after_json_deserialization()
    {
        var sourceScript = Path.Combine(AppContext.BaseDirectory, "TestAssets", "一键停止前后端.ps1");
        Assert.True(File.Exists(sourceScript), $"Stop script not found: {sourceScript}");

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"ModernWMS-stop-script-test-{Guid.NewGuid():N}");
        var scriptsDirectory = Directory.CreateDirectory(Path.Combine(temporaryRoot, "scripts"));
        var testScript = Path.Combine(scriptsDirectory.FullName, "一键停止前后端.ps1");
        File.Copy(sourceScript, testScript);

        var stateKeyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(temporaryRoot.ToLowerInvariant()));
        var stateKey = Convert.ToHexString(stateKeyBytes)[..12].ToLowerInvariant();
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), $"ModernWMS-development-{stateKey}");
        Directory.CreateDirectory(runtimeDirectory);
        var statePath = Path.Combine(runtimeDirectory, "processes.json");

        Process? trackedProcess = null;
        try
        {
            trackedProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                ArgumentList = { "-NoProfile", "-Command", "Start-Sleep -Seconds 120" },
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(trackedProcess);

            var state = new
            {
                repositoryRoot = temporaryRoot,
                createdAtUtc = DateTime.UtcNow.ToString("O"),
                backend = new
                {
                    pid = trackedProcess.Id,
                    startTimeUtc = trackedProcess.StartTime.ToUniversalTime().ToString("O"),
                    port = 0,
                    portOwnershipConfirmed = false
                },
                frontend = (object?)null,
                logDirectory = Path.Combine(runtimeDirectory, "logs")
            };
            File.WriteAllText(statePath, JsonSerializer.Serialize(state));

            using var stopProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", testScript },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            Assert.NotNull(stopProcess);
            var standardOutput = stopProcess.StandardOutput.ReadToEnd();
            var standardError = stopProcess.StandardError.ReadToEnd();
            Assert.True(stopProcess.WaitForExit(20_000), "Stop script did not finish within 20 seconds.");
            Assert.Equal(0, stopProcess.ExitCode);

            Assert.True(
                trackedProcess.WaitForExit(5_000),
                $"Tracked process was not stopped. stdout: {standardOutput} stderr: {standardError}");
        }
        finally
        {
            if (trackedProcess is { HasExited: false })
            {
                trackedProcess.Kill(entireProcessTree: true);
                trackedProcess.WaitForExit();
            }
            trackedProcess?.Dispose();

            if (Directory.Exists(runtimeDirectory))
            {
                Directory.Delete(runtimeDirectory, recursive: true);
            }
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }
}
