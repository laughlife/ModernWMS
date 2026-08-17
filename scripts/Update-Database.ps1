[CmdletBinding()]
param(
    [switch]$Apply,

    [switch]$ConfirmDevelopmentDatabase,

    [string]$FlywayPath = $env:MODERNWMS_FLYWAY_PATH,

    [string]$Url = $env:FLYWAY_URL,

    [string]$User = $env:FLYWAY_USER
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedFlywayVersion = '11.15.0'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$migrationDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'flyway\sql'))

function Resolve-FlywayExecutable {
    if ($FlywayPath) {
        if (-not (Test-Path -LiteralPath $FlywayPath -PathType Leaf)) {
            throw "Flyway executable not found: $FlywayPath"
        }

        return [System.IO.Path]::GetFullPath($FlywayPath)
    }

    $command = Get-Command 'flyway.cmd' -ErrorAction SilentlyContinue
    if (-not $command) {
        $command = Get-Command 'flyway' -ErrorAction SilentlyContinue
    }
    if (-not $command) {
        throw "Flyway $expectedFlywayVersion was not found. Follow flyway/README.md and set -FlywayPath or MODERNWMS_FLYWAY_PATH. This script never downloads or runs an unknown version."
    }

    return $command.Source
}

function Assert-ConnectionConfiguration {
    if (-not $ConfirmDevelopmentDatabase) {
        throw 'This script is restricted to a local development database. Re-run with -ConfirmDevelopmentDatabase after checking the target URL.'
    }
    if ([string]::IsNullOrWhiteSpace($Url)) {
        throw 'Database URL is required. Set FLYWAY_URL (jdbc:mysql://...) or pass -Url.'
    }
    if (-not $Url.StartsWith('jdbc:mysql://', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'FLYWAY_URL/-Url must be a MySQL JDBC URL beginning with jdbc:mysql://.'
    }
    try {
        $databaseUri = [System.Uri]::new($Url.Substring(5))
    }
    catch {
        throw "FLYWAY_URL/-Url is not a valid MySQL JDBC URL: $Url"
    }
    if (-not $databaseUri.IsLoopback) {
        throw "Only a loopback development database is allowed. Refusing host '$($databaseUri.Host)'. Production and remote migrations require a separate reviewed release process."
    }
    if ([string]::IsNullOrWhiteSpace($User)) {
        throw 'Migration user is required. Set FLYWAY_USER or pass -User.'
    }
    if ([string]::IsNullOrWhiteSpace($env:FLYWAY_PASSWORD)) {
        throw 'Migration password is required in the process-only FLYWAY_PASSWORD environment variable. Never store it in the repository or a command-line argument.'
    }
}

function Assert-FlywayVersion {
    param([Parameter(Mandatory = $true)][string]$Executable)

    $global:LASTEXITCODE = 0
    $versionOutput = (& $Executable -v 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read Flyway version (exit code $LASTEXITCODE): $versionOutput"
    }

    $escapedVersion = [Regex]::Escape($expectedFlywayVersion)
    if ($versionOutput -notmatch "(?<![0-9.])$escapedVersion(?![0-9.])") {
        throw "Unsupported Flyway version. Expected $expectedFlywayVersion; actual output: $versionOutput"
    }
}

function Invoke-FlywayCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$Command
    )

    $location = $migrationDirectory.Replace('\', '/')
    $arguments = @(
        "-url=$Url",
        "-user=$User",
        "-locations=filesystem:$location",
        '-table=wms_flyway_schema_history',
        '-cleanDisabled=true',
        '-baselineOnMigrate=false',
        '-outOfOrder=false',
        '-validateMigrationNaming=true',
        $Command
    )

    Write-Host "[Flyway] $Command"
    $global:LASTEXITCODE = 0
    & $Executable @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Flyway $Command failed (exit code $LASTEXITCODE)."
    }
}

try {
    Assert-ConnectionConfiguration
    if (-not (Test-Path -LiteralPath $migrationDirectory -PathType Container)) {
        throw "Migration directory not found: $migrationDirectory"
    }

    $flywayExecutable = Resolve-FlywayExecutable
    Assert-FlywayVersion -Executable $flywayExecutable

    Invoke-FlywayCommand -Executable $flywayExecutable -Command 'info'
    Invoke-FlywayCommand -Executable $flywayExecutable -Command 'validate'

    if ($Apply) {
        Write-Host '[Flyway] Applying migrations was explicitly authorized.'
        Invoke-FlywayCommand -Executable $flywayExecutable -Command 'migrate'
    }
    else {
        Write-Host '[Flyway] Read-only inspection completed. -Apply was not supplied; no migration was run.'
    }
}
catch {
    [Console]::Error.WriteLine("[Flyway failed] $($_.Exception.Message)")
    exit 1
}
