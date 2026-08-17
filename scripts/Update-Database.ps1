[CmdletBinding()]
param(
    [switch]$Apply,

    [switch]$BaselineExisting,

    [string]$ConfirmExistingSchemaFingerprint,

    [switch]$ConfirmDevelopmentDatabase,

    [string]$FlywayPath = $env:MODERNWMS_FLYWAY_PATH,

    [string]$Url = $env:FLYWAY_URL,

    [string]$User = $env:FLYWAY_USER,

    [string]$MySqlPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedFlywayVersion = '11.15.0'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$migrationDirectory = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'flyway\sql'))
$baselineManifestPath = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'flyway\wms-baseline-manifest.json'))
$script:databaseUri = $null

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
    if ($databaseUri.AbsolutePath.Trim('/') -cne 'ruoyi-vue-pro') {
        throw "Only the local ruoyi-vue-pro development database is allowed. Refusing database '$($databaseUri.AbsolutePath.Trim('/'))'."
    }
    if ($Apply -and $BaselineExisting) {
        throw '-Apply and -BaselineExisting are mutually exclusive.'
    }
    if ([string]::IsNullOrWhiteSpace($User)) {
        throw 'Migration user is required. Set FLYWAY_USER or pass -User.'
    }
    if ([string]::IsNullOrWhiteSpace($env:FLYWAY_PASSWORD)) {
        throw 'Migration password is required in the process-only FLYWAY_PASSWORD environment variable. Never store it in the repository or a command-line argument.'
    }
    $script:databaseUri = $databaseUri
}

function Resolve-MySqlExecutable {
    if ($MySqlPath) {
        if (-not (Test-Path -LiteralPath $MySqlPath -PathType Leaf)) {
            throw "MySQL client not found: $MySqlPath"
        }
        return [System.IO.Path]::GetFullPath($MySqlPath)
    }
    $command = Get-Command 'mysql.exe' -ErrorAction SilentlyContinue
    if (-not $command) { $command = Get-Command 'mysql' -ErrorAction SilentlyContinue }
    if (-not $command) {
        throw 'The MySQL client is required for the existing-schema fingerprint check. Pass -MySqlPath.'
    }
    return $command.Source
}

function Invoke-MySqlReadOnly {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$Sql,
        [switch]$Raw
    )
    $arguments = @('--default-character-set=utf8mb4', '--batch', '--skip-column-names')
    if ($Raw) { $arguments += '--raw' }
    $arguments += @(
        "--host=$($script:databaseUri.Host)",
        "--port=$(if ($script:databaseUri.Port -gt 0) { $script:databaseUri.Port } else { 3306 })",
        "--user=$User",
        '--database=ruoyi-vue-pro',
        "--execute=$Sql"
    )
    $previousPassword = $env:MYSQL_PWD
    $previousOutputEncoding = $OutputEncoding
    try {
        $env:MYSQL_PWD = $env:FLYWAY_PASSWORD
        $OutputEncoding = [Text.UTF8Encoding]::new($false)
        $global:LASTEXITCODE = 0
        $output = & $Executable @arguments
        if ($LASTEXITCODE -ne 0) { throw "Read-only MySQL inspection failed (exit code $LASTEXITCODE)." }
        return $output
    }
    finally {
        if ($null -eq $previousPassword) { Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue }
        else { $env:MYSQL_PWD = $previousPassword }
        $OutputEncoding = $previousOutputEncoding
    }
}

function Get-CreateTableFingerprint {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$TableName
    )
    if ($TableName -notmatch '^wms_[a-z0-9_]+$') { throw "Unsafe WMS table name in manifest: $TableName" }
    $output = (Invoke-MySqlReadOnly -Executable $Executable -Sql "SHOW CREATE TABLE ``$TableName``;" -Raw | Out-String)
    $tabIndex = $output.IndexOf("`t")
    if ($tabIndex -lt 0) { throw "Unexpected SHOW CREATE output for $TableName." }
    $createSql = $output.Substring($tabIndex + 1).Trim()
    $canonical = [Regex]::Replace([Regex]::Replace($createSql, ' AUTO_INCREMENT=\d+', ''), '\s+', ' ').Trim()
    $bytes = [Text.Encoding]::UTF8.GetBytes($canonical)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Assert-ExistingSchemaMatchesBaseline {
    if (-not (Test-Path -LiteralPath $baselineManifestPath -PathType Leaf)) {
        throw "Baseline manifest not found: $baselineManifestPath"
    }
    $manifest = Get-Content -Raw -LiteralPath $baselineManifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -ne '1' -or $manifest.algorithm -ne 'SHA256') {
        throw 'Unsupported WMS baseline manifest format.'
    }
    $expected = @($manifest.tables.PSObject.Properties.Name | Sort-Object)
    $mysqlExecutable = Resolve-MySqlExecutable
    $tableQuery = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE' AND TABLE_NAME LIKE 'wms\_%' ESCAPE '\\' AND TABLE_NAME NOT IN ('wms_ef_migrations_history','wms_flyway_schema_history') ORDER BY TABLE_NAME;"
    $actual = @(Invoke-MySqlReadOnly -Executable $mysqlExecutable -Sql $tableQuery | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ([string]::Join("`n", $actual) -cne [string]::Join("`n", $expected)) {
        throw "Existing WMS table set does not match V1. Expected $($expected.Count), actual $($actual.Count). Baseline was not written."
    }
    foreach ($tableName in $expected) {
        $actualHash = Get-CreateTableFingerprint -Executable $mysqlExecutable -TableName $tableName
        $expectedHash = [string]$manifest.tables.$tableName
        if ($actualHash -cne $expectedHash) {
            throw "Existing table $tableName does not match the reviewed V1 structure. Baseline was not written."
        }
    }
    Write-Host "[Flyway] Existing schema fingerprint matches V1 ($($expected.Count) WMS tables)."
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
        [Parameter(Mandatory = $true)][string]$Command,
        [string[]]$ExtraArguments = @()
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
        '-validateMigrationNaming=true'
    )
    $arguments += $ExtraArguments
    $arguments += $Command

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
    if ($BaselineExisting) {
        if ($ConfirmExistingSchemaFingerprint -cne 'WMS_SCHEMA_MATCHES_V1') {
            throw "Existing schema baseline requires -ConfirmExistingSchemaFingerprint 'WMS_SCHEMA_MATCHES_V1'."
        }
        Assert-ExistingSchemaMatchesBaseline
        Write-Host '[Flyway] Recording the explicitly reviewed existing schema at baseline version 1.'
        Invoke-FlywayCommand -Executable $flywayExecutable -Command 'baseline' -ExtraArguments @(
            '-baselineVersion=1',
            '-baselineDescription=existing_wms_schema_reviewed'
        )
        Invoke-FlywayCommand -Executable $flywayExecutable -Command 'info'
        Invoke-FlywayCommand -Executable $flywayExecutable -Command 'validate'
    }
    else {
        Invoke-FlywayCommand -Executable $flywayExecutable -Command 'validate'
    }

    if ($Apply) {
        Write-Host '[Flyway] Applying migrations was explicitly authorized.'
        Invoke-FlywayCommand -Executable $flywayExecutable -Command 'migrate'
    }
    elseif (-not $BaselineExisting) {
        Write-Host '[Flyway] Read-only inspection completed. -Apply was not supplied; no migration was run.'
    }
}
catch {
    [Console]::Error.WriteLine("[Flyway failed] $($_.Exception.Message)")
    exit 1
}
