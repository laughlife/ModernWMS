[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishScript = Join-Path $PSScriptRoot '一键压缩发布包.ps1'
$zipPath = Join-Path $repositoryRoot 'artifacts\publish\wms.zip'

foreach ($requiredPath in @($publishScript, $zipPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "缺少发布验收文件：$requiredPath"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $topLevelEntries = @($entries |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Split('/')[0] } |
        Sort-Object -Unique)
    $expectedTopLevelEntries = @('backend', 'frontend', 'RELEASE_NOTES.txt')
    if (@(Compare-Object -ReferenceObject $expectedTopLevelEntries -DifferenceObject $topLevelEntries).Count -gt 0) {
        throw "发布包顶层结构错误：$($topLevelEntries -join ', ')"
    }

    foreach ($requiredEntry in @(
        'backend/ModernWMS.dll',
        'backend/ModernWMS.deps.json',
        'backend/ModernWMS.runtimeconfig.json',
        'backend/appsettings.json',
        'backend/appsettings.Production.json',
        'backend/nlog.config',
        'frontend/index.html',
        'frontend/unity/index.html',
        'RELEASE_NOTES.txt'
    )) {
        if ($entries -notcontains $requiredEntry) {
            throw "发布包缺少：$requiredEntry"
        }
    }
    if (-not ($entries | Where-Object { $_ -like 'frontend/assets/*' } | Select-Object -First 1)) {
        throw '发布包 frontend/assets/ 为空。'
    }
    if (-not ($entries | Where-Object { $_ -like 'frontend/unity/Build/*' } | Select-Object -First 1)) {
        throw '发布包缺少 Unity WebGL Build 内容。'
    }

    $unexpectedEntry = $entries | Where-Object {
        $_ -match '(^|/)(deploy|nginx|systemd|node_modules|\.git|src|bin|obj|tests?|test-results)(/|$)' -or
        $_ -match '(^|/)(wsm\.nyamtn\.conf|wms\.zip)$' -or
        $_ -match '\.(service|pem|crt|cer|key|pfx|p12)$' -or
        $_ -like 'frontend/dist/*'
    } | Select-Object -First 1
    if ($null -ne $unexpectedEntry) {
        throw "发布包包含禁止文件：$unexpectedEntry"
    }

    $productionConfigEntry = $archive.GetEntry('backend/appsettings.Production.json')
    $productionConfigReader = [IO.StreamReader]::new($productionConfigEntry.Open())
    try {
        $productionConfig = $productionConfigReader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $productionConfigReader.Dispose()
    }
    if ([string]$productionConfig.Urls -ne 'http://127.0.0.1:21011') {
        throw '发布包后端未固定监听 http://127.0.0.1:21011。'
    }
    $productionConnectionStrings = $productionConfig.PSObject.Properties['ConnectionStrings']
    if ($null -ne $productionConnectionStrings) {
        $connectionValue = @($productionConnectionStrings.Value.PSObject.Properties | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_.Value)
        } | Select-Object -First 1)
        if ($connectionValue.Count -gt 0) {
            throw '发布包 appsettings.Production.json 包含数据库连接信息。'
        }
    }
    $productionTokenSettings = $productionConfig.PSObject.Properties['TokenSettings']
    if ($null -ne $productionTokenSettings -and
        $null -ne $productionTokenSettings.Value.PSObject.Properties['SigningKey'] -and
        -not [string]::IsNullOrWhiteSpace([string]$productionTokenSettings.Value.SigningKey)) {
        throw '发布包 appsettings.Production.json 包含签名密钥。'
    }

    $baseConfigEntry = $archive.GetEntry('backend/appsettings.json')
    $baseConfigReader = [IO.StreamReader]::new($baseConfigEntry.Open())
    try {
        $baseConfig = $baseConfigReader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $baseConfigReader.Dispose()
    }
    $baseConnectionStrings = $baseConfig.PSObject.Properties['ConnectionStrings']
    if ($null -ne $baseConnectionStrings) {
        $connectionValue = @($baseConnectionStrings.Value.PSObject.Properties | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_.Value)
        } | Select-Object -First 1)
        if ($connectionValue.Count -gt 0) {
            throw '发布包 appsettings.json 包含数据库连接信息。'
        }
    }
    $baseTokenSettings = $baseConfig.PSObject.Properties['TokenSettings']
    if ($null -ne $baseTokenSettings -and
        $null -ne $baseTokenSettings.Value.PSObject.Properties['SigningKey'] -and
        -not [string]::IsNullOrWhiteSpace([string]$baseTokenSettings.Value.SigningKey)) {
        throw '发布包 appsettings.json 包含签名密钥。'
    }
}
finally {
    $archive.Dispose()
}

Write-Host '生产发布包结构、必要文件、监听配置和敏感配置验证通过。'
