[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$FrontendOrigin,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BackendBaseUrl
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-AbsoluteHttpUri {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value,

        [Parameter(Mandatory = $true)]
        [string]$ParameterName
    )

    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        ($uri.Scheme -ne 'http' -and $uri.Scheme -ne 'https')) {
        throw "$ParameterName 必须是完整的 HTTP 或 HTTPS 地址，当前值：$Value"
    }

    return $uri
}

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

$frontendUri = Get-AbsoluteHttpUri -Value $FrontendOrigin -ParameterName 'FrontendOrigin'
if ($frontendUri.AbsolutePath -ne '/' -or $frontendUri.Query -or $frontendUri.Fragment) {
    throw "FrontendOrigin 只能包含协议、域名和端口，不能包含路径、查询参数或片段：$FrontendOrigin"
}

$backendUri = Get-AbsoluteHttpUri -Value $BackendBaseUrl -ParameterName 'BackendBaseUrl'
if ($backendUri.Query -or $backendUri.Fragment) {
    throw "BackendBaseUrl 不能包含查询参数或片段：$BackendBaseUrl"
}
$normalizedFrontendOrigin = $frontendUri.GetLeftPart([UriPartial]::Authority)
$normalizedBackendBaseUrl = $backendUri.AbsoluteUri.TrimEnd('/')

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = Join-Path $repositoryRoot 'frontend'
$backendProject = Join-Path $repositoryRoot 'backend\ModernWMS\ModernWMS.csproj'
$publishRoot = Join-Path $repositoryRoot 'artifacts\publish'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$packageName = "ModernWMS-prod-$timestamp"
$stagingRoot = Join-Path $publishRoot "$packageName.staging"
$frontendPackageRoot = Join-Path $stagingRoot 'frontend'
$backendPackageRoot = Join-Path $stagingRoot 'backend'
$zipPath = Join-Path $publishRoot "$packageName.zip"
$resolvedPublishRoot = [IO.Path]::GetFullPath($publishRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedStagingRoot = [IO.Path]::GetFullPath($stagingRoot)

if (-not $resolvedStagingRoot.StartsWith($resolvedPublishRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "临时发布目录超出允许范围：$resolvedStagingRoot"
}

foreach ($requiredCommand in @('npm.cmd', 'dotnet')) {
    if (-not (Get-Command $requiredCommand -ErrorAction SilentlyContinue)) {
        throw "未找到命令 $requiredCommand，请先安装对应的 Node.js/npm 或 .NET SDK。"
    }
}

if (-not (Test-Path -LiteralPath $frontendRoot -PathType Container)) {
    throw "前端目录不存在：$frontendRoot"
}
if (-not (Test-Path -LiteralPath $backendProject -PathType Leaf)) {
    throw "后端项目不存在：$backendProject"
}
if ((Test-Path -LiteralPath $stagingRoot) -or
    (Test-Path -LiteralPath $zipPath)) {
    throw "本次发布目标已存在，请稍后重新执行：$packageName"
}

$frontendProductionEnv = Join-Path $frontendRoot '.env.production'
$frontendProductionEnvContent = Get-Content -LiteralPath $frontendProductionEnv -Raw
if ($frontendProductionEnvContent -match '(?m)^[ \t]*VITE_SERVER_PORT[ \t]*=[ \t]*\S+') {
    throw 'frontend/.env.production 中的 VITE_SERVER_PORT 必须为空，正式后端地址由 BackendBaseUrl 完整提供。'
}

New-Item -ItemType Directory -Path $frontendPackageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $backendPackageRoot -Force | Out-Null

$environmentVariableNames = @(
    'ENV',
    'VITE_BASE_PATH',
    'VITE_SERVER_PORT'
)
$previousEnvironment = @{}
foreach ($name in $environmentVariableNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    [Environment]::SetEnvironmentVariable('ENV', 'production', 'Process')
    [Environment]::SetEnvironmentVariable('VITE_BASE_PATH', $normalizedBackendBaseUrl, 'Process')
    [Environment]::SetEnvironmentVariable('VITE_SERVER_PORT', '', 'Process')

    Push-Location $frontendRoot
    try {
        Invoke-CheckedCommand -Command 'npm.cmd' -Arguments @('ci')
        Invoke-CheckedCommand -Command 'npm.cmd' -Arguments @('run', 'build', '--', '--mode', 'production')
    }
    finally {
        Pop-Location
    }

    $frontendDist = Join-Path $frontendRoot 'dist'
    if (-not (Test-Path -LiteralPath $frontendDist -PathType Container)) {
        throw "前端构建完成后未找到 dist 目录：$frontendDist"
    }
    Get-ChildItem -LiteralPath $frontendDist -Force |
        Copy-Item -Destination $frontendPackageRoot -Recurse -Force

    $publishArguments = @(
        'publish',
        $backendProject,
        '--configuration', 'Release',
        '--framework', 'net10.0',
        '--runtime', 'linux-x64',
        '--self-contained', 'false',
        '--output', $backendPackageRoot
    )
    Invoke-CheckedCommand -Command 'dotnet' -Arguments $publishArguments

    $productionConfigPath = Join-Path $backendPackageRoot 'appsettings.Production.json'
    if (-not (Test-Path -LiteralPath $productionConfigPath -PathType Leaf)) {
        throw "后端发布目录缺少生产配置：$productionConfigPath"
    }

    $productionConfig = Get-Content -LiteralPath $productionConfigPath -Raw | ConvertFrom-Json
    $productionConfig.Cors.AllowedOrigins = @($normalizedFrontendOrigin)
    $productionConfig |
        ConvertTo-Json -Depth 20 |
        Set-Content -LiteralPath $productionConfigPath -Encoding utf8

    Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
        throw "ZIP 包生成失败：$zipPath"
    }
    Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force
}
catch {
    if (Test-Path -LiteralPath $stagingRoot) {
        Write-Warning "发布失败，未完成内容保留在：$stagingRoot"
    }
    throw
}
finally {
    foreach ($name in $environmentVariableNames) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
}

Write-Host "发布完成。"
Write-Host "ZIP 包：$zipPath"
Write-Host "前端来源：$normalizedFrontendOrigin"
Write-Host "后端地址：$normalizedBackendBaseUrl"
