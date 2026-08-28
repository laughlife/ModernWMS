[CmdletBinding()]
param(
    [string]$Project,

    [ValidateRange(1, 65535)]
    [int]$Port = 21011,

    [ValidateRange(1, 65535)]
    [int]$FrontendPort = 81,

    [string]$StatePath,

    [string]$LogDirectory,

    [ValidateRange(10, 3600)]
    [int]$IntervalSeconds = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Project)) {
    $Project = Join-Path $repositoryRoot 'backend\ModernWMS\ModernWMS.csproj'
}
if ([string]::IsNullOrWhiteSpace($StatePath) -or [string]::IsNullOrWhiteSpace($LogDirectory)) {
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
    if ([string]::IsNullOrWhiteSpace($StatePath)) {
        $StatePath = Join-Path $runtimeDirectory 'processes.json'
    }
    if ([string]::IsNullOrWhiteSpace($LogDirectory)) {
        $LogDirectory = Join-Path $runtimeDirectory 'logs'
    }
}
$backendRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent (Split-Path -Parent $Project)))
$frontendDirectory = Join-Path $repositoryRoot 'frontend'
$viteCliPath = Join-Path $frontendDirectory 'node_modules\vite\bin\vite.js'
$healthUrl = "http://127.0.0.1:$Port/health"

function Write-WatcherLog {
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Host ("[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message)
}

function Get-SourceFingerprint {
    param([Parameter(Mandatory = $true)][string]$Root)

    $files = Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj)(\\|$)' -and
            $_.FullName -notmatch '\\\.git(\\|$)' -and
            ($_.Extension -eq '.cs' -or $_.Extension -eq '.csproj' -or
             ($_.Extension -eq '.json' -and $_.Name -like 'appsettings*.json'))
        }
    $maxTicks = [long]0
    $count = 0
    foreach ($file in $files) {
        if ($file.LastWriteTimeUtc.Ticks -gt $maxTicks) {
            $maxTicks = $file.LastWriteTimeUtc.Ticks
        }
        $count++
    }
    return "$count|$maxTicks"
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
        }
    }

    return $null
}

function Wait-PortReleased {
    param(
        [Parameter(Mandatory = $true)][int]$TargetPort,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($null -eq (Get-PortOwner -Port $TargetPort)) {
            return $true
        }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Test-BackendHealthy {
    param([int]$TimeoutSeconds = 30)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return $true
            }
        }
        catch {
        }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Get-ListenerEntry {
    param([Parameter(Mandatory = $true)][int]$TargetPort)

    $owner = Get-PortOwner -Port $TargetPort
    if ($null -eq $owner) {
        return $null
    }
    $process = Get-Process -Id $owner.ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return $null
    }
    $executablePath = $null
    try {
        $executablePath = $process.Path
    }
    catch {
        $executablePath = $null
    }
    return [ordered]@{
        pid = $process.Id
        startTimeUtc = $process.StartTime.ToUniversalTime().ToString('O')
        processName = $process.ProcessName
        executablePath = $executablePath
    }
}

function Update-StateListener {
    if (-not (Test-Path -LiteralPath $StatePath)) {
        Write-WatcherLog "状态文件不存在，跳过 listener 更新：$StatePath"
        return
    }

    $state = $null
    try {
        $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
    }
    catch {
        Write-WatcherLog "读取状态文件失败，跳过 listener 更新：$($_.Exception.Message)"
        return
    }
    if ($null -eq $state.backend) {
        Write-WatcherLog '状态文件缺少 backend 条目，跳过 listener 更新。'
        return
    }

    $listener = Get-ListenerEntry -TargetPort $Port
    if ($null -eq $listener) {
        Write-WatcherLog "端口 $Port 未找到监听进程，跳过 listener 更新。"
        return
    }

    $state.backend | Add-Member -NotePropertyName 'listener' -NotePropertyValue $listener -Force
    $state.backend | Add-Member -NotePropertyName 'portOwnershipConfirmed' -NotePropertyValue $true -Force
    $tempPath = "$StatePath.tmp"
    try {
        $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $tempPath -Encoding UTF8
        Move-Item -LiteralPath $tempPath -Destination $StatePath -Force
        Write-WatcherLog "状态文件已更新：后端监听 PID $($listener.pid)。"
    }
    catch {
        Write-WatcherLog "写入状态文件失败：$($_.Exception.Message)"
    }
}

function Update-StateFrontend {
    if (-not (Test-Path -LiteralPath $StatePath)) {
        return
    }

    $state = $null
    try {
        $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
    }
    catch {
        Write-WatcherLog "读取状态文件失败，跳过前端条目更新：$($_.Exception.Message)"
        return
    }

    $listener = Get-ListenerEntry -TargetPort $FrontendPort
    if ($null -eq $listener) {
        Write-WatcherLog "前端端口 $FrontendPort 未找到监听进程，跳过前端条目更新。"
        return
    }

    $frontendEntry = [ordered]@{
        pid = $frontendProcess.Id
        startTimeUtc = $frontendProcess.StartTime.ToUniversalTime().ToString('O')
        port = $FrontendPort
        portOwnershipConfirmed = $true
        listener = $listener
    }
    $state | Add-Member -NotePropertyName 'frontend' -NotePropertyValue ([pscustomobject]$frontendEntry) -Force
    $tempPath = "$StatePath.tmp"
    try {
        $state | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $tempPath -Encoding UTF8
        Move-Item -LiteralPath $tempPath -Destination $StatePath -Force
        Write-WatcherLog "状态文件已更新：前端监听 PID $($listener.pid)。"
    }
    catch {
        Write-WatcherLog "写入前端状态失败：$($_.Exception.Message)"
    }
}

function Stop-AppProcess {
    param($Process)

    if ($null -eq $Process) {
        return
    }
    try {
        if (-not $Process.HasExited) {
            & "$env:SystemRoot\System32\taskkill.exe" /PID $Process.Id /T /F 2>$null | Out-Null
        }
    }
    catch {
    }
}

function Start-AppProcess {
    param([Parameter(Mandatory = $true)][string]$DotnetPath)

    $env:ASPNETCORE_URLS = "http://0.0.0.0:$Port"
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Cors__AllowedOrigins__6 = "http://localhost:$FrontendPort"
    $env:Cors__AllowedOrigins__7 = "http://127.0.0.1:$FrontendPort"
    $env:Cors__AllowedOrigins__8 = "http://192.168.100.102:$FrontendPort"

    return Start-Process -FilePath $DotnetPath `
        -ArgumentList @('run', '--project', $Project, '--no-launch-profile', '--no-restore') `
        -WorkingDirectory $repositoryRoot `
        -NoNewWindow `
        -PassThru
}

function Start-FrontendProcess {
    param([Parameter(Mandatory = $true)][string]$NodePath)

    if (-not (Test-Path -LiteralPath $viteCliPath)) {
        Write-WatcherLog "前端依赖未安装（$viteCliPath 不存在），跳过前端启动。请先在 frontend 运行 npm ci。"
        return $null
    }

    $previousViteBasePath = [Environment]::GetEnvironmentVariable('VITE_BASE_PATH', 'Process')
    $previousViteServerPort = [Environment]::GetEnvironmentVariable('VITE_SERVER_PORT', 'Process')
    $previousViteCliPort = [Environment]::GetEnvironmentVariable('VITE_CLI_PORT', 'Process')
    try {
        $env:VITE_BASE_PATH = 'http://127.0.0.1'
        $env:VITE_SERVER_PORT = [string]$Port
        $env:VITE_CLI_PORT = [string]$FrontendPort
        return Start-Process -FilePath $NodePath `
            -ArgumentList @($viteCliPath, '--host', '0.0.0.0', '--port', [string]$FrontendPort, '--strictPort') `
            -WorkingDirectory $frontendDirectory `
            -NoNewWindow `
            -PassThru
    }
    finally {
        [Environment]::SetEnvironmentVariable('VITE_BASE_PATH', $previousViteBasePath, 'Process')
        [Environment]::SetEnvironmentVariable('VITE_SERVER_PORT', $previousViteServerPort, 'Process')
        [Environment]::SetEnvironmentVariable('VITE_CLI_PORT', $previousViteCliPort, 'Process')
    }
}

function Initialize-DevelopmentState {
    $existing = $null
    $stateExists = Test-Path -LiteralPath $StatePath
    if ($stateExists) {
        try {
            $existing = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
        }
        catch {
            Write-WatcherLog "警告：状态文件损坏，将覆盖：$StatePath"
            $existing = $null
        }
    }

    $selfManaged = $false
    if ($null -ne $existing -and $null -ne $existing.backend) {
        try {
            $selfManaged = ([int]$existing.backend.pid -eq $PID)
        }
        catch {
            $selfManaged = $false
        }
    }

    if (-not $selfManaged) {
        $conflictPid = 0
        if ($null -ne $existing -and $null -ne $existing.backend) {
            try {
                $backendPid = [int]$existing.backend.pid
            }
            catch {
                $backendPid = 0
            }
            if ($backendPid -gt 0 -and $backendPid -ne $PID) {
                $proc = Get-Process -Id $backendPid -ErrorAction SilentlyContinue
                if ($null -ne $proc) {
                    $startTimeMatch = $false
                    try {
                        $startTimeMatch = [string]::Equals(
                            $proc.StartTime.ToUniversalTime().ToString('O'),
                            [string]$existing.backend.startTimeUtc,
                            [System.StringComparison]::OrdinalIgnoreCase)
                    }
                    catch {
                        $startTimeMatch = $false
                    }
                    if ($startTimeMatch) {
                        $conflictPid = $backendPid
                    }
                }
            }
        }
        if ($conflictPid -gt 0) {
            throw "检测到已有后端控制进程（PID $conflictPid）在运行。请先运行 scripts\一键停止前后端.ps1 或从 Rider 停止后再启动。"
        }

        $owner = Get-PortOwner -Port $Port
        if ($null -ne $owner) {
            throw "后端端口 $Port 已被占用：PID $($owner.ProcessId) ($($owner.ProcessName))。请先停止占用进程。"
        }
    }

    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null

    if (-not $selfManaged) {
        $backendEntry = [ordered]@{
            pid = $PID
            startTimeUtc = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('O')
            port = $Port
            portOwnershipConfirmed = $false
        }
        # 保留已有状态中的前端条目，避免单独运行本脚本时把前端跟踪信息覆盖丢失。
        $frontendEntry = if ($null -ne $existing -and $null -ne $existing.frontend) { $existing.frontend } else { $null }
        $tempPath = "$StatePath.tmp"
        [ordered]@{
            repositoryRoot = $repositoryRoot
            createdAtUtc = [DateTime]::UtcNow.ToString('O')
            backend = $backendEntry
            frontend = $frontendEntry
            logDirectory = $LogDirectory
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $tempPath -Encoding UTF8
        Move-Item -LiteralPath $tempPath -Destination $StatePath -Force
        Write-WatcherLog "状态文件已初始化：控制进程 PID $PID，后端端口 $Port，前端端口 $FrontendPort，日志 $LogDirectory。"
    }
}

Write-WatcherLog "后端变更检测已启动：项目 $Project，端口 $Port，检测间隔 $IntervalSeconds 秒。"
Write-WatcherLog '重启规则：检测到源码变更后，等待源码连续 1 个检测周期无变化，且距上次重启满 1 个检测周期，才自动重启。'

$dotnetCommand = (Get-Command 'dotnet' -ErrorAction Stop).Source
$nodeCommand = (Get-Command 'node.exe' -ErrorAction Stop).Source
$appProcess = $null
$frontendProcess = $null
$lastFingerprint = $null
$pendingRestart = $false
$stableSinceUtc = $null
$lastRestartUtc = $null

Initialize-DevelopmentState

try {
    while ($true) {
        try {
            $fingerprint = Get-SourceFingerprint -Root $backendRoot

            if ($null -ne $lastFingerprint -and $fingerprint -ne $lastFingerprint) {
                $pendingRestart = $true
                $stableSinceUtc = Get-Date
                Write-WatcherLog '检测到源码变更，等待源码稳定后自动重启。'
            }
            $lastFingerprint = $fingerprint

            $appAlive = ($null -ne $appProcess) -and (-not $appProcess.HasExited)
            $now = Get-Date
            $rateLimited = ($null -ne $lastRestartUtc) -and (($now - $lastRestartUtc).TotalSeconds -lt $IntervalSeconds)
            $quietPeriodOk = (-not $pendingRestart) -or
                (($null -ne $stableSinceUtc) -and (($now - $stableSinceUtc).TotalSeconds -ge $IntervalSeconds))

            $shouldStart = $false
            $reason = ''
            if ($null -eq $appProcess) {
                $shouldStart = $true
                $reason = '初始启动'
            }
            elseif (-not $appAlive) {
                if (-not $rateLimited) {
                    $shouldStart = $true
                    $reason = '进程已退出，自动恢复'
                }
            }
            elseif ($pendingRestart -and $quietPeriodOk -and (-not $rateLimited)) {
                $shouldStart = $true
                $reason = '源码已稳定且限频间隔已满，自动重启'
            }

            if ($shouldStart) {
                Write-WatcherLog "触发重启：$reason"
                Stop-AppProcess -Process $appProcess
                if (-not (Wait-PortReleased -TargetPort $Port)) {
                    Write-WatcherLog "警告：端口 $Port 未在超时时间内释放，仍尝试启动。"
                }
                $appProcess = Start-AppProcess -DotnetPath $dotnetCommand
                $pendingRestart = $false
                $stableSinceUtc = $null
                $lastRestartUtc = Get-Date
                if (Test-BackendHealthy) {
                    Write-WatcherLog '健康检查通过，后端已就绪。'
                    Update-StateListener
                }
                else {
                    Write-WatcherLog '警告：健康检查未通过，将在下一轮重试。'
                }
            }

            # 确保前端 vite 始终运行：端口未监听则（重新）启动，避免重启后端后前端掉线。
            $frontendListening = ($null -ne (Get-PortOwner -Port $FrontendPort))
            if (-not $frontendListening) {
                Write-WatcherLog "前端未监听端口 $FrontendPort，正在启动前端 vite..."
                $frontendProcess = Start-FrontendProcess -NodePath $nodeCommand
                if ($null -ne $frontendProcess) {
                    $frontendDeadline = (Get-Date).AddSeconds(15)
                    while ((Get-Date) -lt $frontendDeadline -and $null -eq (Get-PortOwner -Port $FrontendPort)) {
                        Start-Sleep -Milliseconds 500
                    }
                    if ($null -ne (Get-PortOwner -Port $FrontendPort)) {
                        Write-WatcherLog '前端已就绪。'
                        Update-StateFrontend
                    }
                    else {
                        Write-WatcherLog '警告：前端未在超时时间内监听，将在下一轮重试。'
                    }
                }
            }
        }
        catch {
            Write-WatcherLog "本轮处理失败，继续运行：$($_.Exception.Message)"
        }

        Start-Sleep -Seconds $IntervalSeconds
    }
}
finally {
    Write-WatcherLog '变更检测已停止，正在清理后端与前端进程...'
    Stop-AppProcess -Process $appProcess
    Stop-AppProcess -Process $frontendProcess
    Write-WatcherLog '清理完成。'
}
