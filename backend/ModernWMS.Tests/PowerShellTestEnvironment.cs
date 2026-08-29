namespace ModernWMS.Tests;

internal static class PowerShellTestEnvironment
{
    public static string? Executable { get; } = FindExecutable();

    private static string? FindExecutable()
    {
        var candidates = OperatingSystem.IsWindows()
            ? new[] { "powershell.exe", "pwsh.exe", "pwsh" }
            : new[] { "pwsh", "pwsh.exe" };
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        foreach (var candidate in candidates)
        {
            var executable = Path.Combine(directory, candidate);
            if (File.Exists(executable)) return executable;
        }
        return null;
    }
}

internal sealed class PowerShellFactAttribute : FactAttribute
{
    public PowerShellFactAttribute()
    {
        if (PowerShellTestEnvironment.Executable == null)
            Skip = "Requires PowerShell (pwsh or Windows PowerShell).";
    }
}
