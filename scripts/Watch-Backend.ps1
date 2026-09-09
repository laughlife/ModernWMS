#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Project,

    [ValidateRange(1, 65535)]
    [int]$Port = 21011,

    [ValidateRange(1, 65535)]
    [int]$FrontendPort = 81,

    [string]$StatePath,

    [string]$LogDirectory,

    [ValidateRange(1, 3600)]
    [int]$IntervalSeconds = 5,

    [ValidateRange(1, 3600)]
    [int]$QuietPeriodSeconds = 60,

    [ValidateRange(30, 1800)]
    [int]$StartupTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Port -eq $FrontendPort) { throw '后端端口和前端端口不能相同。' }

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

    # 在遍历前排除生成目录；逐文件内容哈希可识别保留时间戳的修改和重命名。
    $directories = [System.Collections.Generic.Stack[string]]::new()
    $directories.Push($Root)
    $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    while ($directories.Count -gt 0) {
        foreach ($item in (Get-ChildItem -LiteralPath $directories.Pop() -Force -ErrorAction Stop)) {
            if ($item.PSIsContainer) {
                if ($item.Name -notin @('bin', 'obj', '.git', '.codex', '.idea', 'node_modules') -and
                    -not ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
                    $directories.Push($item.FullName)
                }
            }
            elseif ($item.Extension -in @('.cs', '.csproj', '.props', '.targets', '.resx') -or
                $item.Name -like 'appsettings*.json' -or $item.Name -eq 'nlog.config') {
                $files.Add($item)
            }
        }
    }
    foreach ($name in @('global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')) {
        $path = Join-Path $repositoryRoot $name
        if (Test-Path -LiteralPath $path) {
            $files.Add((Get-Item -LiteralPath $path -ErrorAction Stop))
        }
    }
    $snapshot = foreach ($file in ($files | Sort-Object FullName)) {
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256 -ErrorAction Stop).Hash
        '{0}|{1}|{2}|{3}' -f $file.FullName, $file.Length, $file.LastWriteTimeUtc.Ticks, $hash
    }
    return ($snapshot -join "`n")
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
    param([int]$TimeoutSeconds = $StartupTimeoutSeconds)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($appProcess.HasExited) { return $false }
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
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
    $controller = if ($TargetPort -eq $Port) { $appProcess } else { $frontendProcess }
    if ($null -eq $controller -or $controller.HasExited -or
        $process.StartTime.ToUniversalTime() -lt $controller.StartTime.ToUniversalTime()) {
        throw "端口 $TargetPort 不属于当前服务进程，拒绝认领 PID $($process.Id)。"
    }
    if ($TargetPort -eq $FrontendPort) {
        if ($process.Id -ne $controller.Id) {
            throw "前端端口被其他进程占用，拒绝认领 PID $($process.Id)。"
        }
    }
    else {
        # dotnet run 的监听者是子进程；验证祖先链，不能仅凭端口/名称认领。
        $ancestorId = $process.Id
        $owned = $false
        for ($depth = 0; $depth -lt 16 -and $ancestorId -gt 0; $depth++) {
            if ($ancestorId -eq $controller.Id) { $owned = $true; break }
            $ancestor = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $ancestorId" -ErrorAction Stop
            if ($null -eq $ancestor) { break }
            $ancestorId = [int]$ancestor.ParentProcessId
        }
        if (-not $owned) { throw "后端端口不属于本次 dotnet 进程树，拒绝认领 PID $($process.Id)。" }
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
        -ArgumentList @('run', '--project', ('"{0}"' -f $Project), '--no-launch-profile', '--no-restore') `
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
            -ArgumentList @(('"{0}"' -f $viteCliPath), '--host', '0.0.0.0', '--port', [string]$FrontendPort, '--strictPort') `
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
    if (Test-Path -LiteralPath $StatePath) {
        $existing = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json
        if (-not [string]::Equals([string]$existing.repositoryRoot, $repositoryRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw '状态文件不属于当前仓库，拒绝覆盖。'
        }
        foreach ($service in @($existing.backend, $existing.frontend)) {
            if ($null -eq $service) { continue }
            $targets = @($service)
            if ($service.PSObject.Properties['listener']) { $targets += $service.listener }
            foreach ($entry in $targets) {
                if ($null -eq $entry) { continue }
                $process = Get-Process -Id ([int]$entry.pid) -ErrorAction SilentlyContinue
                if ($null -ne $process -and [string]::Equals(
                    $process.StartTime.ToUniversalTime().ToString('O'),
                    (ConvertTo-ProcessStartTimeUtcString -Value $entry.startTimeUtc),
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "已有本仓库进程 PID $($entry.pid) 运行，请先执行一键停止前后端.ps1。"
                }
            }
        }
    }
    foreach ($targetPort in @($Port, $FrontendPort)) {
        $owner = Get-PortOwner -Port $targetPort
        if ($null -ne $owner) {
            throw "端口 $targetPort 已被 PID $($owner.ProcessId) 占用，拒绝认领或终止。"
        }
    }

    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path -Parent $StatePath) -Force | Out-Null
    $tempPath = "$StatePath.tmp"
    [ordered]@{
        repositoryRoot = $repositoryRoot
        createdAtUtc = [DateTime]::UtcNow.ToString('O')
        backend = [ordered]@{
            pid = $PID
            startTimeUtc = (Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('O')
            port = $Port
            portOwnershipConfirmed = $false
        }
        frontend = $null
        logDirectory = $LogDirectory
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $tempPath -Encoding UTF8
    Move-Item -LiteralPath $tempPath -Destination $StatePath -Force
    Write-WatcherLog "状态文件已初始化：控制进程 PID $PID，后端端口 $Port，前端端口 $FrontendPort。"
}
function ConvertTo-ProcessStartTimeUtcString {
    param($Value)
    if ($Value -is [DateTimeOffset]) { return $Value.UtcDateTime.ToString('O') }
    if ($Value -is [DateTime]) { return $Value.ToUniversalTime().ToString('O') }
    return [string]$Value
}

Write-WatcherLog "后端变更检测已启动：项目 $Project，端口 $Port，检测间隔 $IntervalSeconds 秒。"
Write-WatcherLog "重启规则：每 $IntervalSeconds 秒扫描后端；每次检测到变更都重新计时，连续 $QuietPeriodSeconds 秒无变化才重启。前端由 Vite 热更新。"

$dotnetCommand = (Get-Command 'dotnet' -ErrorAction Stop).Source
$nodeCommand = (Get-Command 'node.exe' -ErrorAction Stop).Source
$appProcess = $null
$frontendProcess = $null
$lastFingerprint = $null
$pendingRestart = $false
$stableSinceUtc = $null
$lastRestartUtc = $null

# 独立运行 watcher 时也必须保持单实例；此锁与启动/停止事务锁分离。
$watcherHash = [System.Security.Cryptography.SHA256]::Create()
try {
    $watcherKey = ([BitConverter]::ToString($watcherHash.ComputeHash(
        [System.Text.Encoding]::UTF8.GetBytes($repositoryRoot.ToLowerInvariant()))) -replace '-', '').Substring(0, 12).ToLowerInvariant()
}
finally { $watcherHash.Dispose() }
$watcherMutex = [System.Threading.Mutex]::new($false, "Local\ModernWMS-watcher-$watcherKey")
$watcherMutexAcquired = $false

try {
    try { $watcherMutexAcquired = $watcherMutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $watcherMutexAcquired = $true }
    if (-not $watcherMutexAcquired) { throw '本仓库已有变更检测进程，拒绝重复启动。' }
    Initialize-DevelopmentState
    while ($true) {
        try {
            try {
                $fingerprint = Get-SourceFingerprint -Root $backendRoot
            }
            catch {
                # 读取失败不代表源码稳定，恢复可读后仍须等待完整安静期。
                $pendingRestart = $true
                $stableSinceUtc = Get-Date
                throw
            }

            if ($null -ne $lastFingerprint -and $fingerprint -ne $lastFingerprint) {
                $pendingRestart = $true
                $stableSinceUtc = Get-Date
                Write-WatcherLog '检测到源码变更，等待源码稳定后自动重启。'
            }
            $lastFingerprint = $fingerprint

            $appAlive = ($null -ne $appProcess) -and (-not $appProcess.HasExited)
            $now = Get-Date
            $rateLimited = ($null -ne $lastRestartUtc) -and (($now - $lastRestartUtc).TotalSeconds -lt $QuietPeriodSeconds)
            $quietPeriodOk = (-not $pendingRestart) -or
                (($null -ne $stableSinceUtc) -and (($now - $stableSinceUtc).TotalSeconds -ge $QuietPeriodSeconds))

            $shouldStart = $false
            $reason = ''
            if ($null -eq $appProcess -and $quietPeriodOk -and (-not $rateLimited)) {
                $shouldStart = $true
                $reason = '初始启动'
            }
            elseif (-not $appAlive) {
                if ($quietPeriodOk -and (-not $rateLimited)) {
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
                    throw "端口 $Port 未在超时时间内释放，取消本轮启动，不终止或认领占用进程。"
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
                    Stop-AppProcess -Process $appProcess
                    $pendingRestart = $true
                    $stableSinceUtc = Get-Date
                    throw '健康检查未通过，已停止本轮后端，等待安静期后重试。'
                }
            }

            # 确保前端 vite 始终运行：端口未监听则（重新）启动，避免重启后端后前端掉线。
            $frontendListening = ($null -ne (Get-PortOwner -Port $FrontendPort))
            if ($frontendListening) {
                # 已占用也必须证明是本监控器启动的 node，不能视为任意服务已就绪。
                $null = Get-ListenerEntry -TargetPort $FrontendPort
            }
            else {
                Stop-AppProcess -Process $frontendProcess
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
                        Stop-AppProcess -Process $frontendProcess
                        throw '前端未在超时时间内监听，已停止本轮进程，稍后重试。'
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
    if ($watcherMutexAcquired) { $watcherMutex.ReleaseMutex() }
    $watcherMutex.Dispose()
    Write-WatcherLog '清理完成。'
}
