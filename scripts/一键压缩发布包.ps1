[CmdletBinding()]
param(
    [Parameter()]
    [ValidatePattern('^[0-9A-Za-z][0-9A-Za-z._-]*$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter()]
        [string[]]$Arguments = @()
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "命令执行失败（退出码 $LASTEXITCODE）：$Command $($Arguments -join ' ')"
    }
}

function Set-ProcessEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Value
    )

    [Environment]::SetEnvironmentVariable($Name, $Value, 'Process')
}

function Clear-PackagedSecrets {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigPath,

        [Parameter()]
        [switch]$SetListener
    )

    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $connectionStringsProperty = $config.PSObject.Properties['ConnectionStrings']
    if ($null -ne $connectionStringsProperty) {
        foreach ($property in @($connectionStringsProperty.Value.PSObject.Properties)) {
            $property.Value = ''
        }
    }
    $tokenSettingsProperty = $config.PSObject.Properties['TokenSettings']
    if ($null -ne $tokenSettingsProperty -and
        $null -ne $tokenSettingsProperty.Value.PSObject.Properties['SigningKey']) {
        $tokenSettingsProperty.Value.SigningKey = ''
    }
    if ($SetListener) {
        $config | Add-Member -NotePropertyName 'Urls' -NotePropertyValue 'http://127.0.0.1:21011' -Force
    }
    $config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $ConfigPath -Encoding utf8
}

function Assert-NoPackagedSecrets {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigPath
    )

    $config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $connectionStringsProperty = $config.PSObject.Properties['ConnectionStrings']
    if ($null -ne $connectionStringsProperty) {
        foreach ($property in @($connectionStringsProperty.Value.PSObject.Properties)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                throw "通用发布包配置中不得包含数据库连接信息：$ConfigPath"
            }
        }
    }
    $tokenSettingsProperty = $config.PSObject.Properties['TokenSettings']
    if ($null -ne $tokenSettingsProperty -and
        $null -ne $tokenSettingsProperty.Value.PSObject.Properties['SigningKey'] -and
        -not [string]::IsNullOrWhiteSpace([string]$tokenSettingsProperty.Value.SigningKey)) {
        throw "通用发布包配置中不得包含签名密钥：$ConfigPath"
    }
}

function Get-ZipEntryNames {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        return @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    }
    finally {
        $archive.Dispose()
    }
}

function Invoke-UnzipChecks {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $nativeUnzip = Get-Command 'unzip' -ErrorAction SilentlyContinue
    if ($null -ne $nativeUnzip) {
        Invoke-CheckedCommand -Command $nativeUnzip.Source -Arguments @('-t', $Path)
        Invoke-CheckedCommand -Command $nativeUnzip.Source -Arguments @('-l', $Path)
        return
    }

    $wsl = Get-Command 'wsl.exe' -ErrorAction SilentlyContinue
    if ($null -eq $wsl) {
        throw '未找到 unzip，也未找到可用于执行 unzip 的 WSL。'
    }

    $wslUnzip = (& $wsl.Source -e sh -lc 'command -v unzip').Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($wslUnzip)) {
        throw 'WSL 中未找到 unzip。'
    }
    $wslZipPath = (& $wsl.Source -e wslpath -a -u $Path).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($wslZipPath)) {
        throw "无法将 ZIP 路径转换为 WSL 路径：$Path"
    }

    Invoke-CheckedCommand -Command $wsl.Source -Arguments @('-e', $wslUnzip, '-t', $wslZipPath)
    Invoke-CheckedCommand -Command $wsl.Source -Arguments @('-e', $wslUnzip, '-l', $wslZipPath)
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = Join-Path $repositoryRoot 'frontend'
$backendProject = Join-Path $repositoryRoot 'backend\ModernWMS\ModernWMS.csproj'
$backendSourceRoot = Join-Path $repositoryRoot 'backend'
$flywaySqlRoot = Join-Path $repositoryRoot 'flyway\sql'
$databaseUpdateScript = Join-Path $PSScriptRoot 'Update-Database.ps1'
$publishRoot = Join-Path $repositoryRoot 'artifacts\publish'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$packageName = "ModernWMS-prod-$timestamp"
$stagingRoot = Join-Path $publishRoot "$packageName.staging"
$frontendBuildRoot = Join-Path $publishRoot "$packageName.frontend-build"
$frontendPackageRoot = Join-Path $stagingRoot 'frontend'
$backendPackageRoot = Join-Path $stagingRoot 'backend'
$releaseNotesPath = Join-Path $stagingRoot 'RELEASE_NOTES.txt'
$zipPath = Join-Path $publishRoot 'wms.zip'
$zipTempPath = Join-Path $publishRoot "$packageName.tmp.zip"
$resolvedPublishRoot = [IO.Path]::GetFullPath($publishRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedStagingRoot = [IO.Path]::GetFullPath($stagingRoot)
$resolvedFrontendBuildRoot = [IO.Path]::GetFullPath($frontendBuildRoot)
$resolvedZipPath = [IO.Path]::GetFullPath($zipPath)

foreach ($publishPath in @($resolvedStagingRoot, $resolvedFrontendBuildRoot, $resolvedZipPath)) {
    if (-not $publishPath.StartsWith($resolvedPublishRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "发布路径超出允许范围：$publishPath"
    }
}

foreach ($requiredCommand in @('npm.cmd', 'dotnet', 'git')) {
    if (-not (Get-Command $requiredCommand -ErrorAction SilentlyContinue)) {
        throw "未找到命令 $requiredCommand。"
    }
}
foreach ($requiredPath in @($frontendRoot, $backendSourceRoot, $flywaySqlRoot)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Container)) {
        throw "发布所需目录不存在：$requiredPath"
    }
}
foreach ($requiredPath in @($backendProject, $databaseUpdateScript)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "发布所需文件不存在：$requiredPath"
    }
}

$healthMapping = Get-ChildItem -LiteralPath $backendSourceRoot -Recurse -Filter '*.cs' -File |
    Select-String -SimpleMatch 'MapHealthChecks("/health"' |
    Select-Object -First 1
if ($null -eq $healthMapping) {
    throw '后端源码未找到 /health 健康检查映射，停止生成生产发布包。'
}

if ((Test-Path -LiteralPath $stagingRoot) -or
    (Test-Path -LiteralPath $frontendBuildRoot) -or
    (Test-Path -LiteralPath $zipTempPath)) {
    throw "本次发布目标已存在，请稍后重新执行：$packageName"
}

$commitId = (& git -C $repositoryRoot rev-parse --short=12 HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commitId)) {
    throw '无法读取当前 Git 提交号。'
}
$workingTreeChanges = @(& git -C $repositoryRoot status --porcelain --untracked-files=no)
if ($LASTEXITCODE -ne 0) {
    throw '无法读取当前 Git 工作区状态。'
}
if ($workingTreeChanges.Count -gt 0) {
    $commitId = "$commitId-dirty"
}
$changeNotes = @(& git -C $repositoryRoot log -10 --pretty=format:'%s')
if ($LASTEXITCODE -ne 0) {
    throw '无法生成发布功能变更记录。'
}
$migrationFiles = @(Get-ChildItem -LiteralPath $flywaySqlRoot -Filter 'V*__*.sql' -File | Sort-Object Name)
$databaseScriptContent = Get-Content -LiteralPath $databaseUpdateScript -Raw
$flywayVersionMatch = [Regex]::Match($databaseScriptContent, "expectedFlywayVersion\s*=\s*'([^']+)'")
if (-not $flywayVersionMatch.Success) {
    throw '无法从 Update-Database.ps1 读取所需 Flyway 版本。'
}
$flywayVersion = $flywayVersionMatch.Groups[1].Value
$buildTime = Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz'

New-Item -ItemType Directory -Path $frontendPackageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $backendPackageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $frontendBuildRoot -Force | Out-Null

$previousViteBasePath = [Environment]::GetEnvironmentVariable('VITE_BASE_PATH', 'Process')
$previousViteServerPort = [Environment]::GetEnvironmentVariable('VITE_SERVER_PORT', 'Process')
$previousViteBaseApi = [Environment]::GetEnvironmentVariable('VITE_BASE_API', 'Process')

try {
    Get-ChildItem -LiteralPath $frontendRoot -Force |
        Where-Object { $_.Name -notin @('node_modules', 'dist', 'test-results', 'artifacts', '.git') } |
        Copy-Item -Destination $frontendBuildRoot -Recurse -Force

    Set-ProcessEnvironmentValue -Name 'VITE_BASE_PATH' -Value '/api/'
    Set-ProcessEnvironmentValue -Name 'VITE_SERVER_PORT' -Value ''
    Set-ProcessEnvironmentValue -Name 'VITE_BASE_API' -Value '/api/'

    Push-Location $frontendBuildRoot
    try {
        Invoke-CheckedCommand -Command 'npm.cmd' -Arguments @('ci')
        Invoke-CheckedCommand -Command 'npm.cmd' -Arguments @('run', 'build', '--', '--mode', 'production')
    }
    finally {
        Pop-Location
    }

    $frontendDist = Join-Path $frontendBuildRoot 'dist'
    if (-not (Test-Path -LiteralPath $frontendDist -PathType Container)) {
        throw "前端构建完成后未找到全新 dist 目录：$frontendDist"
    }
    Get-ChildItem -LiteralPath $frontendDist -Force |
        Copy-Item -Destination $frontendPackageRoot -Recurse -Force

    $frontendIndex = Join-Path $frontendPackageRoot 'index.html'
    $frontendAssets = Join-Path $frontendPackageRoot 'assets'
    $unityIndex = Join-Path $frontendPackageRoot 'unity\index.html'
    $unityBuild = Join-Path $frontendPackageRoot 'unity\Build'
    foreach ($requiredFrontendPath in @($frontendIndex, $unityIndex)) {
        if (-not (Test-Path -LiteralPath $requiredFrontendPath -PathType Leaf)) {
            throw "前端发布产物缺少：$requiredFrontendPath"
        }
    }
    foreach ($requiredFrontendPath in @($frontendAssets, $unityBuild)) {
        if (-not (Test-Path -LiteralPath $requiredFrontendPath -PathType Container)) {
            throw "前端发布产物缺少：$requiredFrontendPath"
        }
    }
    if (@(Get-ChildItem -LiteralPath $unityBuild -File -Recurse).Count -eq 0) {
        throw 'Unity WebGL Build 目录为空。'
    }
    $frontendTextFiles = @(Get-ChildItem -LiteralPath $frontendPackageRoot -File -Recurse |
        Where-Object { $_.Extension -in @('.html', '.js', '.css') })
    $absoluteApiReference = $frontendTextFiles | Select-String -Pattern 'https?://[^"''\s]+/api/?' | Select-Object -First 1
    if ($null -ne $absoluteApiReference) {
        throw "前端生产产物包含绝对 API 地址：$($absoluteApiReference.Path)"
    }
    $relativeApiReference = $frontendTextFiles | Select-String -SimpleMatch '/api' | Select-Object -First 1
    if ($null -eq $relativeApiReference) {
        throw '前端生产产物中未找到相对 API 路径 /api/。'
    }

    $publishArguments = @(
        'publish',
        $backendProject,
        '--configuration', 'Release',
        '--framework', 'net10.0',
        '--runtime', 'linux-x64',
        '--self-contained', 'false',
        '--output', $backendPackageRoot,
        '-p:UseAppHost=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false'
    )
    Invoke-CheckedCommand -Command 'dotnet' -Arguments $publishArguments

    $requiredBackendFiles = @(
        'ModernWMS.dll',
        'ModernWMS.deps.json',
        'ModernWMS.runtimeconfig.json',
        'appsettings.json',
        'appsettings.Production.json',
        'nlog.config'
    )
    foreach ($requiredBackendFile in $requiredBackendFiles) {
        $requiredBackendPath = Join-Path $backendPackageRoot $requiredBackendFile
        if (-not (Test-Path -LiteralPath $requiredBackendPath -PathType Leaf)) {
            throw "后端发布产物缺少：$requiredBackendFile"
        }
    }

    $runtimeConfig = Get-Content -LiteralPath (Join-Path $backendPackageRoot 'ModernWMS.runtimeconfig.json') -Raw | ConvertFrom-Json
    $runtimeFrameworkNames = @($runtimeConfig.runtimeOptions.frameworks | ForEach-Object { [string]$_.name })
    if ([string]$runtimeConfig.runtimeOptions.tfm -ne 'net10.0' -or
        $runtimeFrameworkNames -notcontains 'Microsoft.AspNetCore.App') {
        throw '后端发布产物不是 .NET 10 framework-dependent ASP.NET Core 应用。'
    }

    $developmentConfig = Join-Path $backendPackageRoot 'appsettings.Development.json'
    if (Test-Path -LiteralPath $developmentConfig) {
        Remove-Item -LiteralPath $developmentConfig -Force
    }
    Get-ChildItem -LiteralPath $backendPackageRoot -Filter '*.pdb' -File -Recurse | Remove-Item -Force

    $baseConfigPath = Join-Path $backendPackageRoot 'appsettings.json'
    $productionConfigPath = Join-Path $backendPackageRoot 'appsettings.Production.json'
    Clear-PackagedSecrets -ConfigPath $baseConfigPath
    Clear-PackagedSecrets -ConfigPath $productionConfigPath -SetListener
    Assert-NoPackagedSecrets -ConfigPath $baseConfigPath
    Assert-NoPackagedSecrets -ConfigPath $productionConfigPath

    $publishedProductionConfig = Get-Content -LiteralPath $productionConfigPath -Raw | ConvertFrom-Json
    if ([string]$publishedProductionConfig.Urls -ne 'http://127.0.0.1:21011') {
        throw '后端监听地址未固定为 http://127.0.0.1:21011。'
    }

    $releaseLines = [Collections.Generic.List[string]]::new()
    $releaseLines.Add("Version: $Version")
    $releaseLines.Add("BuildTime: $buildTime")
    $releaseLines.Add("Commit: $commitId")
    $releaseLines.Add('Changes:')
    foreach ($changeNote in $changeNotes) {
        if (-not [string]::IsNullOrWhiteSpace($changeNote)) {
            $releaseLines.Add("- $changeNote")
        }
    }
    if ($migrationFiles.Count -eq 0) {
        $releaseLines.Add('DatabaseChanges: none')
    }
    else {
        $releaseLines.Add('DatabaseChanges: required')
        $releaseLines.Add("FlywayVersion: $flywayVersion")
        $releaseLines.Add("SqlFiles: $($migrationFiles.Name -join ', ')")
    }
    $releaseLines | Set-Content -LiteralPath $releaseNotesPath -Encoding utf8

    if (Test-Path -LiteralPath $resolvedZipPath -PathType Leaf) {
        Remove-Item -LiteralPath $resolvedZipPath -Force
    }
    Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipTempPath -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $zipTempPath -PathType Leaf)) {
        throw "ZIP 临时包生成失败：$zipTempPath"
    }

    $zipEntries = Get-ZipEntryNames -Path $zipTempPath
    $topLevelEntries = @($zipEntries |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Split('/')[0] } |
        Sort-Object -Unique)
    $expectedTopLevelEntries = @('backend', 'frontend', 'RELEASE_NOTES.txt')
    $topLevelDifference = @(Compare-Object -ReferenceObject $expectedTopLevelEntries -DifferenceObject $topLevelEntries)
    if ($topLevelDifference.Count -gt 0) {
        throw "ZIP 顶层结构不合规，实际为：$($topLevelEntries -join ', ')"
    }

    foreach ($requiredEntry in @(
        'frontend/index.html',
        'frontend/unity/index.html',
        'backend/ModernWMS.dll',
        'backend/ModernWMS.deps.json',
        'backend/ModernWMS.runtimeconfig.json',
        'backend/appsettings.json',
        'backend/appsettings.Production.json',
        'backend/nlog.config',
        'RELEASE_NOTES.txt'
    )) {
        if ($zipEntries -notcontains $requiredEntry) {
            throw "ZIP 内容校验失败，缺少：$requiredEntry"
        }
    }
    if (-not ($zipEntries | Where-Object { $_ -like 'frontend/assets/*' } | Select-Object -First 1)) {
        throw 'ZIP 内容校验失败，frontend/assets/ 为空。'
    }
    if (-not ($zipEntries | Where-Object { $_ -like 'frontend/unity/Build/*' } | Select-Object -First 1)) {
        throw 'ZIP 内容校验失败，frontend/unity/Build/ 为空。'
    }

    $unexpectedEntry = $zipEntries | Where-Object {
        $_ -match '(^|/)(deploy|nginx|systemd|node_modules|\.git|src|bin|obj|tests?|test-results)(/|$)' -or
        $_ -match '(^|/)(wsm\.nyamtn\.conf|wms\.zip)$' -or
        $_ -match '\.(service|pem|crt|cer|key|pfx|p12)$' -or
        $_ -like 'frontend/dist/*' -or
        $_ -like '*.frontend-build/*'
    } | Select-Object -First 1
    if ($null -ne $unexpectedEntry) {
        throw "ZIP 内容校验失败，包含禁止发布文件：$unexpectedEntry"
    }

    Move-Item -LiteralPath $zipTempPath -Destination $zipPath
    Invoke-UnzipChecks -Path $zipPath
    Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force
}
catch {
    if (Test-Path -LiteralPath $zipTempPath) {
        Remove-Item -LiteralPath $zipTempPath -Force
    }
    if (Test-Path -LiteralPath $stagingRoot) {
        Write-Warning "发布失败，未完成内容保留在：$stagingRoot"
    }
    throw
}
finally {
    Set-ProcessEnvironmentValue -Name 'VITE_BASE_PATH' -Value $(if ($null -eq $previousViteBasePath) { '' } else { $previousViteBasePath })
    Set-ProcessEnvironmentValue -Name 'VITE_SERVER_PORT' -Value $(if ($null -eq $previousViteServerPort) { '' } else { $previousViteServerPort })
    Set-ProcessEnvironmentValue -Name 'VITE_BASE_API' -Value $(if ($null -eq $previousViteBaseApi) { '' } else { $previousViteBaseApi })
    if (Test-Path -LiteralPath $resolvedFrontendBuildRoot) {
        try {
            Remove-Item -LiteralPath $resolvedFrontendBuildRoot -Recurse -Force
        }
        catch {
            Write-Warning "前端临时构建目录清理失败，可稍后手动删除：$resolvedFrontendBuildRoot"
        }
    }
}

Write-Host '生产发布包生成并校验完成。'
Write-Host "ZIP 包：$zipPath"
Write-Host 'ZIP 顶层：backend/、frontend/、RELEASE_NOTES.txt'
Write-Host '后端启动：dotnet ModernWMS.dll'
Write-Host '后端监听：http://127.0.0.1:21011'
Write-Host '前端 API：/api/'
Write-Host '数据库迁移：仅记录在 RELEASE_NOTES.txt，本脚本不会执行迁移。'
