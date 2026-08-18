[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishScript = Join-Path $PSScriptRoot '一键压缩发布包.ps1'
$legacyPublishScript = Join-Path $PSScriptRoot 'publish-prod.ps1'
$zipPath = Join-Path $repositoryRoot 'artifacts\publish\wms.zip'

if (-not (Test-Path -LiteralPath $publishScript -PathType Leaf)) {
    throw "缺少一键发布脚本：$publishScript"
}
if (Test-Path -LiteralPath $legacyPublishScript) {
    throw "旧发布脚本仍然存在：$legacyPublishScript"
}
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "缺少待验证的发布包：$zipPath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $requiredEntries = @(
        'frontend/index.html',
        'backend/ModernWMS.dll',
        'backend/ModernWMS.runtimeconfig.json',
        'backend/appsettings.Production.json',
        'deploy/nginx/conf.d/wsm.nyamtn.conf',
        '部署说明.txt'
    )
    foreach ($requiredEntry in $requiredEntries) {
        if ($entries -notcontains $requiredEntry) {
            throw "发布包缺少：$requiredEntry"
        }
    }

    $productionConfigEntry = $archive.GetEntry('backend/appsettings.Production.json')
    $reader = [IO.StreamReader]::new($productionConfigEntry.Open())
    try {
        $productionConfig = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    if ([string]$productionConfig.Urls -ne 'http://127.0.0.1:21011') {
        throw '发布包后端未固定监听 http://127.0.0.1:21011。'
    }
}
finally {
    $archive.Dispose()
}

Write-Host '发布脚本名称及发布包部署内容验证通过。'
