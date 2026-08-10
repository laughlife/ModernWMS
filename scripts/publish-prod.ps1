[CmdletBinding()]
param()

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

function Get-DotEnvValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Content,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $escapedName = [Regex]::Escape($Name)
    $match = [Regex]::Match($Content, "(?m)^[ \t]*$escapedName[ \t]*=[ \t]*(.*?)[ \t]*$")
    if (-not $match.Success) {
        throw "生产环境配置缺少 $Name。"
    }

    return $match.Groups[1].Value.Trim().Trim("'", '"')
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = Join-Path $repositoryRoot 'frontend'
$backendProject = Join-Path $repositoryRoot 'backend\ModernWMS\ModernWMS.csproj'
$frontendProductionEnv = Join-Path $frontendRoot '.env.production'
$backendProductionConfig = Join-Path $repositoryRoot 'backend\ModernWMS\appsettings.Production.json'
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
if (-not (Test-Path -LiteralPath $frontendProductionEnv -PathType Leaf)) {
    throw "前端生产配置不存在：$frontendProductionEnv"
}
if (-not (Test-Path -LiteralPath $backendProductionConfig -PathType Leaf)) {
    throw "后端生产配置不存在：$backendProductionConfig"
}
if ((Test-Path -LiteralPath $stagingRoot) -or
    (Test-Path -LiteralPath $zipPath)) {
    throw "本次发布目标已存在，请稍后重新执行：$packageName"
}

$frontendProductionEnvContent = Get-Content -LiteralPath $frontendProductionEnv -Raw
$backendBasePath = Get-DotEnvValue -Content $frontendProductionEnvContent -Name 'VITE_BASE_PATH'
$backendPort = Get-DotEnvValue -Content $frontendProductionEnvContent -Name 'VITE_SERVER_PORT'
if ([string]::IsNullOrWhiteSpace($backendBasePath)) {
    throw '请先在 frontend/.env.production 中配置 VITE_BASE_PATH。'
}
$backendBaseUrl = if ([string]::IsNullOrWhiteSpace($backendPort)) {
    $backendBasePath.TrimEnd('/')
} else {
    "$($backendBasePath.TrimEnd('/')):$backendPort"
}
$backendUri = Get-AbsoluteHttpUri -Value $backendBaseUrl -ParameterName 'frontend/.env.production 后端地址'
if ($backendUri.Query -or $backendUri.Fragment) {
    throw "frontend/.env.production 后端地址不能包含查询参数或片段：$backendBaseUrl"
}
$normalizedBackendBaseUrl = $backendUri.AbsoluteUri.TrimEnd('/')

$productionConfig = Get-Content -LiteralPath $backendProductionConfig -Raw | ConvertFrom-Json
$allowedOrigins = @($productionConfig.Cors.AllowedOrigins)
if ($allowedOrigins.Count -eq 0) {
    throw '请先在 backend/ModernWMS/appsettings.Production.json 的 Cors.AllowedOrigins 中配置正式前端地址。'
}
$normalizedFrontendOrigins = foreach ($allowedOrigin in $allowedOrigins) {
    $frontendUri = Get-AbsoluteHttpUri -Value $allowedOrigin -ParameterName 'Cors.AllowedOrigins'
    if ($frontendUri.AbsolutePath -ne '/' -or $frontendUri.Query -or $frontendUri.Fragment) {
        throw "Cors.AllowedOrigins 只能包含协议、域名和端口：$allowedOrigin"
    }
    $frontendUri.GetLeftPart([UriPartial]::Authority)
}

New-Item -ItemType Directory -Path $frontendPackageRoot -Force | Out-Null
New-Item -ItemType Directory -Path $backendPackageRoot -Force | Out-Null

try {
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

Write-Host "发布完成。"
Write-Host "ZIP 包：$zipPath"
Write-Host "前端来源：$($normalizedFrontendOrigins -join ', ')"
Write-Host "后端地址：$normalizedBackendBaseUrl"
