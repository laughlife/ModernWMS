[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$BackendPort = 21011,

    [ValidateRange(1, 65535)]
    [int]$FrontendPort = 80,

    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$backendProject = Join-Path $repositoryRoot 'backend\ModernWMS\ModernWMS.csproj'
$frontendDirectory = Join-Path $repositoryRoot 'frontend'
$stateKeyBytes = [System.Text.Encoding]::UTF8.GetBytes($repositoryRoot.ToLowerInvariant())
$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    $stateKeyHash = $sha256.ComputeHash($stateKeyBytes)
}
finally {
    $sha256.Dispose()
}
$stateKey = ([System.BitConverter]::ToString($stateKeyHash) -replace '-', '').Substring(0, 12).ToLowerInvariant()
$runtimeDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "ModernWMS-development-$stateKey"
$statePath = Join-Path $runtimeDirectory 'processes.json'
$logDirectory = Join-Path $runtimeDirectory 'logs'
$mutexName = "Local\ModernWMS-development-$stateKey"

function Get-ProcessStartTimeUtcString {
    param([Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process)

    return $Process.StartTime.ToUniversalTime().ToString('O')
}

function Test-TrackedProcess {
    param($Entry)

    if (-not $Entry -or -not $Entry.pid -or -not $Entry.startTimeUtc) {
        return $false
    }

    $process = Get-Process -Id ([int]$Entry.pid) -ErrorAction SilentlyContinue
    if (-not $process) {
        return $false
    }

    try {
        return [string]::Equals(
            (Get-ProcessStartTimeUtcString -Process $process),
            [string]$Entry.startTimeUtc,
            [System.StringComparison]::OrdinalIgnoreCase)
    }
    catch {
        return $false
    }
}

function Get-ServiceListenerEntry {
    param($Entry)

    if (-not $Entry) {
        return $null
    }

    try {
        return $Entry.listener
    }
    catch {
        return $null
    }
}

function Test-TrackedService {
    param($Entry)

    return (Test-TrackedProcess -Entry $Entry) -or
        (Test-TrackedProcess -Entry (Get-ServiceListenerEntry -Entry $Entry))
}

function Get-DevelopmentState {
    if (-not (Test-Path -LiteralPath $statePath)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    }
    catch {
        throw "开发进程状态文件损坏：$statePath。请确认没有启动中的本项目进程后删除该文件。"
    }
}

function Get-PortOwner {
    param([Parameter(Mandatory = $true)][int]$Port)

    $lines = & "$env:SystemRoot\System32\netstat.exe" -ano -p tcp 2>$null
    foreach ($line in $lines) {
        if ($line -notmatch '^\s*TCP\s+(\S+):(\d+)\s+\S+\s+LISTENING\s+(\d+)\s*$') {
            continue
        }

        if ([int]$Matches[2] -ne $Port) {
            continue
        }

        $processId = [int]$Matches[3]
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        return [pscustomobject]@{
            Port = $Port
            ProcessId = $processId
            ProcessName = if ($process) { $process.ProcessName } else { '<无法读取>' }
            LocalAddress = $Matches[1]
        }
    }

    return $null
}

function New-PortListenerEntry {
    param(
        [Parameter(Mandatory = $true)][int]$Port,
        [int]$ExpectedProcessId = 0,
        [DateTime]$NotBeforeUtc = [DateTime]::MinValue,
        [string]$ExpectedProcessName,
        [string]$ExpectedPathRoot
    )

    $owner = Get-PortOwner -Port $Port
    if (-not $owner) {
        throw "端口 $Port 尚未建立监听。"
    }

    $process = Get-Process -Id $owner.ProcessId -ErrorAction SilentlyContinue
    if (-not $process) {
        throw "无法读取端口 $Port 的监听进程 PID $($owner.ProcessId)。"
    }

    $processStartTimeUtc = $process.StartTime.ToUniversalTime()
    $processPath = try { $process.Path } catch { $null }
    if ($ExpectedProcessId -gt 0 -and $process.Id -ne $ExpectedProcessId) {
        throw "端口 $Port 由非本次启动进程 PID $($process.Id) 监听；期望 PID $ExpectedProcessId。不会跟踪或终止该占用进程。"
    }
    if ($processStartTimeUtc -lt $NotBeforeUtc) {
        throw "端口 $Port 的监听进程早于本次控制进程启动，拒绝认领 PID $($process.Id)。"
    }
    if ($ExpectedProcessName -and -not [string]::Equals(
        $process.ProcessName,
        $ExpectedProcessName,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "端口 $Port 的监听进程名称为 $($process.ProcessName)，期望 $ExpectedProcessName，拒绝认领。"
    }
    if ($ExpectedPathRoot) {
        $resolvedExpectedPathRoot = [System.IO.Path]::GetFullPath($ExpectedPathRoot).TrimEnd('\') + '\'
        if (-not $processPath -or -not $processPath.StartsWith(
            $resolvedExpectedPathRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "端口 $Port 的监听进程路径不属于本仓库后端输出目录，拒绝认领 PID $($process.Id)。"
        }
    }

    return [ordered]@{
        pid = $process.Id
        startTimeUtc = $processStartTimeUtc.ToString('O')
        processName = $process.ProcessName
        executablePath = $processPath
    }
}

function Assert-PortAvailable {
    param(
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$ServiceName
    )

    $owner = Get-PortOwner -Port $Port
    if ($owner) {
        throw "$ServiceName 端口 $Port 已被占用：PID $($owner.ProcessId) ($($owner.ProcessName))，监听地址 $($owner.LocalAddress)。启动器不会终止该进程；请先确认并停止它。"
    }
}

function Assert-CommandAvailable {
    param([Parameter(Mandatory = $true)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "找不到命令 '$Name'，请先安装并配置 PATH。"
    }

    return $command.Source
}

function Save-DevelopmentState {
    param($BackendEntry, $FrontendEntry)

    New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
    [ordered]@{
        repositoryRoot = $repositoryRoot
        createdAtUtc = [DateTime]::UtcNow.ToString('O')
        backend = $BackendEntry
        frontend = $FrontendEntry
        logDirectory = $logDirectory
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $statePath -Encoding UTF8
}

function Stop-StartedServiceProcesses {
    param($Entry)

    if (-not $Entry) {
        return $true
    }

    if (Test-TrackedProcess -Entry $Entry) {
        $taskKillProcess = Start-Process -FilePath "$env:SystemRoot\System32\taskkill.exe" `
            -ArgumentList @('/PID', [string]$Entry.pid, '/T', '/F') `
            -WindowStyle Hidden `
            -Wait `
            -PassThru
        if ($taskKillProcess.ExitCode -ne 0 -and (Test-TrackedProcess -Entry $Entry)) {
            Stop-Process -Id ([int]$Entry.pid) -Force -ErrorAction SilentlyContinue
        }
    }

    $targets = @((Get-ServiceListenerEntry -Entry $Entry))
    $handledProcessIds = @{}
    foreach ($target in $targets) {
        if (-not (Test-TrackedProcess -Entry $target)) {
            continue
        }

        $targetProcessId = [int]$target.pid
        if ($handledProcessIds.ContainsKey($targetProcessId)) {
            continue
        }
        $handledProcessIds[$targetProcessId] = $true
        Stop-Process -Id $targetProcessId -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 250
    return -not (Test-TrackedService -Entry $Entry)
}

function Test-ServicePortReleased {
    param($Entry)

    if (-not $Entry -or -not $Entry.port) {
        return $true
    }

    try {
        if (-not $Entry.portOwnershipConfirmed) {
            return $true
        }
    }
    catch {
        return $true
    }

    return -not (Get-PortOwner -Port ([int]$Entry.port))
}

$mutex = [System.Threading.Mutex]::new($false, $mutexName)
$mutexAcquired = $false
$backendEntry = $null
$frontendEntry = $null
$stateOwnedByThisInvocation = $false

try {
    $mutexAcquired = $mutex.WaitOne([TimeSpan]::FromSeconds(10))
    if (-not $mutexAcquired) {
        throw '另一个 ModernWMS 启动器正在执行，请稍后重试。'
    }

    if (-not (Test-Path -LiteralPath $backendProject)) {
        throw "找不到后端项目：$backendProject"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $frontendDirectory 'package.json'))) {
        throw "找不到前端项目：$frontendDirectory"
    }

    $dotnetCommand = Assert-CommandAvailable -Name 'dotnet'
    $npmCommand = Assert-CommandAvailable -Name 'npm.cmd'
    $nodeCommand = Assert-CommandAvailable -Name 'node.exe'
    $viteCliPath = Join-Path $frontendDirectory 'node_modules\vite\bin\vite.js'

    $requiredAssetFiles = @(
        (Join-Path $repositoryRoot 'backend\ModernWMS\obj\project.assets.json'),
        (Join-Path $repositoryRoot 'backend\ModernWMS.Core\obj\project.assets.json'),
        (Join-Path $repositoryRoot 'backend\ModernWMS.WMS\obj\project.assets.json')
    )
    $missingAssetFiles = @($requiredAssetFiles | Where-Object { -not (Test-Path -LiteralPath $_) })
    if ($missingAssetFiles.Count -gt 0) {
        throw "缺少 NuGet 还原产物。请先运行 dotnet restore backend\ModernWMS.sln，然后重试。缺失：$($missingAssetFiles -join '，')"
    }

    $existingState = Get-DevelopmentState
    if ($existingState) {
        $activeNames = @()
        if (Test-TrackedService -Entry $existingState.backend) { $activeNames += "后端 PID $($existingState.backend.pid)" }
        if (Test-TrackedService -Entry $existingState.frontend) { $activeNames += "前端 PID $($existingState.frontend.pid)" }
        if ($activeNames.Count -gt 0) {
            throw "本仓库已有启动器管理的进程正在运行：$($activeNames -join '，')。请先运行 scripts\Stop-Development.ps1。"
        }

        if (-not $CheckOnly) {
            Remove-Item -LiteralPath $statePath -Force
        }
    }

    Assert-PortAvailable -Port $BackendPort -ServiceName '后端'
    Assert-PortAvailable -Port $FrontendPort -ServiceName '前端'

    if (-not (Test-Path -LiteralPath $viteCliPath)) {
        throw "前端依赖未安装。请先在 $frontendDirectory 运行 npm ci。"
    }

    Write-Host "[检查通过] dotnet: $dotnetCommand"
    Write-Host "[检查通过] npm: $npmCommand"
    Write-Host "[检查通过] node: $nodeCommand"
    Write-Host "[检查通过] 后端端口 $BackendPort、前端端口 $FrontendPort 均可用。"

    if ($CheckOnly) {
        Write-Host '[检查完成] CheckOnly 模式未初始化数据库，也未启动任何进程。'
        return
    }

    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null

    Write-Host '[数据库] 开发启动不检查、不修改数据库；结构变更只通过 scripts\Update-Database.ps1 显式执行。'

    Write-Host "[1/2] 启动后端变更检测（每分钟检测源码，稳定后自动重启）：http://127.0.0.1:$BackendPort"
    $watcherScript = Join-Path $PSScriptRoot 'Watch-Backend.ps1'
    if (-not (Test-Path -LiteralPath $watcherScript)) {
        throw "找不到后端变更检测脚本：$watcherScript"
    }
    $selfExecutable = (Get-Process -Id $PID).Path
    $backendProcess = Start-Process -FilePath $selfExecutable `
        -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', $watcherScript,
            '-Project', $backendProject,
            '-Port', [string]$BackendPort,
            '-FrontendPort', [string]$FrontendPort,
            '-StatePath', $statePath,
            '-LogDirectory', $logDirectory,
            '-IntervalSeconds', '60'
        ) `
        -WorkingDirectory $repositoryRoot `
        -NoNewWindow `
        -PassThru

    $backendEntry = [ordered]@{
        pid = $backendProcess.Id
        startTimeUtc = Get-ProcessStartTimeUtcString -Process $backendProcess
        port = $BackendPort
        portOwnershipConfirmed = $false
    }
    Save-DevelopmentState -BackendEntry $backendEntry -FrontendEntry $null
    $stateOwnedByThisInvocation = $true

    $backendReady = $false
    $healthUrl = "http://127.0.0.1:$BackendPort/health"
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        if ($backendProcess.HasExited) {
            throw "后端启动失败（退出码 $($backendProcess.ExitCode)）。日志：$logDirectory"
        }
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                $backendReady = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }
    if (-not $backendReady) {
        throw "后端在 30 秒内未通过健康检查 $healthUrl。日志：$logDirectory"
    }
    $backendEntry['listener'] = New-PortListenerEntry `
        -Port $BackendPort `
        -NotBeforeUtc $backendProcess.StartTime.ToUniversalTime() `
        -ExpectedProcessName 'ModernWMS' `
        -ExpectedPathRoot (Split-Path -Parent $backendProject)
    $backendEntry['portOwnershipConfirmed'] = $true
    Save-DevelopmentState -BackendEntry $backendEntry -FrontendEntry $null

    Write-Host "[2/2] 等待前端就绪（前端由后端变更检测进程统一管理）：http://127.0.0.1:$FrontendPort"
    $frontendReady = $false
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        if (Get-PortOwner -Port $FrontendPort) {
            $frontendReady = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $frontendReady) {
        throw "前端在 30 秒内未监听端口 $FrontendPort。日志：$logDirectory"
    }

    Write-Host '[启动完成]'
    Write-Host "  前端：http://127.0.0.1:$FrontendPort"
    Write-Host "  后端：http://127.0.0.1:$BackendPort"
    Write-Host '  实时日志：当前控制台'
    Write-Host "  运行状态：$statePath"
    Write-Host '  停止：powershell -ExecutionPolicy Bypass -File scripts\Stop-Development.ps1'
    Write-Host '[日志输出中] 启动器将保持运行；请从另一个控制台执行停止脚本。'

    $backendProcess.WaitForExit()
    if ($backendProcess.ExitCode -eq 0) {
        Write-Host '[运行结束] 后端变更检测进程已正常退出。'
    }
    else {
        Write-Warning "后端变更检测进程已退出，退出码：$($backendProcess.ExitCode)。"
    }
}
catch {
    if ($stateOwnedByThisInvocation) {
        $frontendStopped = Stop-StartedServiceProcesses -Entry $frontendEntry
        $backendStopped = Stop-StartedServiceProcesses -Entry $backendEntry
        $frontendPortReleased = Test-ServicePortReleased -Entry $frontendEntry
        $backendPortReleased = Test-ServicePortReleased -Entry $backendEntry
        $cleanupComplete = $frontendStopped -and $backendStopped -and
            $frontendPortReleased -and $backendPortReleased
        if (Test-Path -LiteralPath $statePath) {
            if ($cleanupComplete) {
                Remove-Item -LiteralPath $statePath -Force
            }
            else {
                [Console]::Error.WriteLine("[清理未完成] 状态文件已保留：$statePath。请运行 scripts\Stop-Development.ps1 重试，勿按进程名批量结束进程。")
            }
        }
    }
    [Console]::Error.WriteLine("[启动失败] $($_.Exception.Message)")
    exit 1
}
finally {
    if ($mutexAcquired) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}
