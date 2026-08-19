[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
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
$mutexName = "Local\ModernWMS-development-$stateKey"

function Get-ProcessStartTimeUtcString {
    param([Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process)

    return $Process.StartTime.ToUniversalTime().ToString('O')
}

function ConvertTo-ProcessStartTimeUtcString {
    param($Value)

    if ($Value -is [DateTimeOffset]) {
        return $Value.UtcDateTime.ToString('O')
    }
    if ($Value -is [DateTime]) {
        return $Value.ToUniversalTime().ToString('O')
    }

    return [string]$Value
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
        $expectedStartTimeUtc = ConvertTo-ProcessStartTimeUtcString -Value $Entry.startTimeUtc
        return [string]::Equals(
            (Get-ProcessStartTimeUtcString -Process $process),
            $expectedStartTimeUtc,
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
            ProcessId = $processId
            ProcessName = if ($process) { $process.ProcessName } else { '<无法读取>' }
        }
    }

    return $null
}

function Stop-TrackedService {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        $Entry
    )

    if (-not $Entry) {
        return $true
    }

    if (Test-TrackedProcess -Entry $Entry) {
        Write-Host "正在停止${Name}控制进程树 PID $($Entry.pid)..."
        $taskKillProcess = Start-Process -FilePath "$env:SystemRoot\System32\taskkill.exe" `
            -ArgumentList @('/PID', [string]$Entry.pid, '/T', '/F') `
            -WindowStyle Hidden `
            -Wait `
            -PassThru
        if ($taskKillProcess.ExitCode -ne 0 -and (Test-TrackedProcess -Entry $Entry)) {
            Stop-Process -Id ([int]$Entry.pid) -Force -ErrorAction SilentlyContinue
        }
    }

    $targets = @(
        [pscustomobject]@{ Role = '监听进程'; Process = (Get-ServiceListenerEntry -Entry $Entry) },
        [pscustomobject]@{ Role = '控制进程兜底'; Process = $Entry }
    )
    $handledProcessIds = @{}
    foreach ($target in $targets) {
        if (-not (Test-TrackedProcess -Entry $target.Process)) {
            continue
        }

        $targetProcessId = [int]$target.Process.pid
        if ($handledProcessIds.ContainsKey($targetProcessId)) {
            continue
        }
        $handledProcessIds[$targetProcessId] = $true
        Write-Host "正在停止$Name$($target.Role) PID $targetProcessId..."
        Stop-Process -Id $targetProcessId -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 500
    return -not (Test-TrackedService -Entry $Entry)
}

$mutex = [System.Threading.Mutex]::new($false, $mutexName)
$mutexAcquired = $false
try {
    $mutexAcquired = $mutex.WaitOne([TimeSpan]::FromSeconds(10))
    if (-not $mutexAcquired) {
        throw '启动器正在执行，请稍后重试停止命令。'
    }

    if (-not (Test-Path -LiteralPath $statePath)) {
        Write-Host '没有找到本仓库启动器管理的开发进程。未终止任何进程。'
        return
    }

    try {
        $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    }
    catch {
        throw "开发进程状态文件损坏：$statePath。为避免误杀进程，启动器不会继续。"
    }

    if (-not [string]::Equals(
        [string]$state.repositoryRoot,
        $repositoryRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "状态文件不属于当前仓库。为避免误杀进程，未执行终止：$statePath"
    }

    $frontendStopped = Stop-TrackedService -Name '前端' -Entry $state.frontend
    $backendStopped = Stop-TrackedService -Name '后端' -Entry $state.backend

    $occupiedPorts = @()
    foreach ($service in @(
        [pscustomobject]@{ Name = '前端'; Entry = $state.frontend },
        [pscustomobject]@{ Name = '后端'; Entry = $state.backend }
    )) {
        if (-not $service.Entry -or -not $service.Entry.port) {
            continue
        }
        try {
            if (-not $service.Entry.portOwnershipConfirmed) {
                continue
            }
        }
        catch {
            continue
        }
        $owner = Get-PortOwner -Port ([int]$service.Entry.port)
        if ($owner) {
            $occupiedPorts += "$($service.Name)端口 $($service.Entry.port) 仍由 PID $($owner.ProcessId) ($($owner.ProcessName)) 监听"
        }
    }

    if (-not $frontendStopped -or -not $backendStopped -or $occupiedPorts.Count -gt 0) {
        $details = if ($occupiedPorts.Count -gt 0) { $occupiedPorts -join '；' } else { '仍有已跟踪进程未退出' }
        throw "停止未完成：$details。状态文件已保留：$statePath"
    }

    Remove-Item -LiteralPath $statePath -Force
    Write-Host '开发进程和监听端口均已停止；状态文件已清理。'
}
finally {
    if ($mutexAcquired) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}
