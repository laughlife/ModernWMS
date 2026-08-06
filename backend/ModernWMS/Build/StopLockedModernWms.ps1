param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$resolvedExecutablePath = [System.IO.Path]::GetFullPath($ExecutablePath)

$lockedProcesses = Get-CimInstance Win32_Process -Filter "Name = 'ModernWMS.exe'" |
    Where-Object {
        $_.ExecutablePath -and
        [string]::Equals(
            [System.IO.Path]::GetFullPath($_.ExecutablePath),
            $resolvedExecutablePath,
            [System.StringComparison]::OrdinalIgnoreCase)
    }

foreach ($lockedProcess in $lockedProcesses) {
    Write-Host "Stopping previous ModernWMS process $($lockedProcess.ProcessId) before build."
    Stop-Process -Id $lockedProcess.ProcessId -Force
}
