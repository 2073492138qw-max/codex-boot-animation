#Requires -Version 5.1
[CmdletBinding()]
param([string]$PluginRoot, [string]$RuntimeRoot, [switch]$RequireIsolatedRuntimeRoot, [ValidateRange(1, 300)][int]$InstallBudgetSeconds = 60)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
if ($env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY) {
    throw 'Open Windows PowerShell from the Start menu and run this script there. The Codex terminal can write to an isolated registry view.'
}
$repo = Split-Path -Parent $PSScriptRoot
if (-not $PluginRoot) { $PluginRoot = Join-Path $repo 'plugins\codex-boot-animation' }
$PluginRoot = (Resolve-Path -LiteralPath $PluginRoot).Path
$exe = Join-Path $PluginRoot 'hooks\BootPlayer.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run scripts\Build.ps1 first.' }
if (-not (Get-ChildItem -LiteralPath (Join-Path $PluginRoot 'videos\新聊天') -Filter '*.mp4' -File -ErrorAction SilentlyContinue)) {
    throw 'The intro video is missing.'
}

# The runtime data root is an explicit input so tests can point installation
# at an isolated directory instead of the real user profile.
if ($RequireIsolatedRuntimeRoot) {
    # Validate the original spelling before GetFullPath can hide relative paths.
    $RuntimeRoot = Resolve-IsolatedRuntimeRoot -RuntimeRoot $RuntimeRoot
}
if (-not $RuntimeRoot) { $RuntimeRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation' }
if (-not [IO.Path]::IsPathRooted($RuntimeRoot)) { throw 'RuntimeRoot must be an absolute directory.' }
$RuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $RuntimeRoot -or -not [IO.Path]::GetFileName($RuntimeRoot)) { throw 'RuntimeRoot must name a directory below a drive root.' }
$defaultRuntimeRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation')).TrimEnd([IO.Path]::DirectorySeparatorChar)
if ($RequireIsolatedRuntimeRoot -and [string]::Equals($RuntimeRoot, $defaultRuntimeRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Tests must pass an isolated -RuntimeRoot. Refused before any change: $RuntimeRoot"
}
New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null

$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$command = '"' + $exe + '" --supervise'
$startup = Get-CompanionStartupStatus -RuntimeRoot $RuntimeRoot
# Validate the intended command, including a fresh registration. A manual-only
# preference with missing/unknown approval must fail before creating a Run value.
Assert-CompanionStartupPolicy -Status $startup -RegisteredCommand $command
$existing = Get-ItemProperty -Path $key -Name 'CodexBootAnimation' -ErrorAction SilentlyContinue
if ($null -ne $existing -and $existing.CodexBootAnimation -ne $command) {
    throw 'Another CodexBootAnimation sign-in entry is already installed. Remove or migrate that installation explicitly before replacing it.'
}
$before = @(Get-CodexCompanionProcesses -Exe $exe | ForEach-Object { $_.ProcessId })
# Snapshot the pause flag: a failed transaction must not silently resume
# automatic animation for a user who had disabled it.
$disabled = Join-Path $RuntimeRoot 'disabled'
$disabledExisted = Test-Path -LiteralPath $disabled -PathType Leaf
$disabledBytes = if ($disabledExisted) { [IO.File]::ReadAllBytes($disabled) } else { $null }
$disabledRemoved = $false
# One absolute budget covers adopt, launch and readiness. A late success is
# still a timeout, and only helpers started by this run are terminated.
$budget = New-InstallBudget -TimeoutSeconds $InstallBudgetSeconds
$stage = 'preflight'
$installMutex = New-Object Threading.Mutex($false, 'Local\CodexBootAnimationInstallV1')
$installLocked = $false
try {
    try { $installLocked = $installMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $installLocked = $true }
    if (-not $installLocked) { throw 'Another installation is already running. Wait for it to finish, then retry.' }
    $startup = Get-CompanionStartupStatus -RuntimeRoot $RuntimeRoot
    Assert-CompanionStartupPolicy -Status $startup -RegisteredCommand $command
    $stage = 'registry'
    New-Item -Path $key -Force | Out-Null
    New-ItemProperty -Path $key -Name 'CodexBootAnimation' -Value $command -PropertyType String -Force | Out-Null
    $saved = (Get-ItemProperty -Path $key -Name 'CodexBootAnimation').CodexBootAnimation
    if ($saved -ne $command) { throw 'Windows sign-in entry verification failed.' }
    $stage = 'pause-flag'
    if ($disabledExisted) {
        Remove-Item -LiteralPath $disabled -ErrorAction Stop
        $disabledRemoved = $true
    }
    # Installing/restarting the helper is not a cold launch of an already-open Codex.
    $stage = 'adopt'
    $remainingMs = Get-InstallRemainingMs -Budget $budget
    if ($remainingMs -le 0) { throw 'The install budget expired before adopting existing Codex processes.' }
    $adopt = Invoke-BoundedCompanion -Exe $exe -Arguments @('--adopt-running') -TimeoutMs $remainingMs
    if ($adopt.TimedOut) { throw 'Adopting existing Codex processes exceeded the install budget.' }
    if ($adopt.ExitCode -ne 0) { throw 'Could not adopt existing Codex processes.' }
    $stage = 'launch'
    Start-Process -FilePath $exe -ArgumentList '--supervise' -WindowStyle Hidden
    $stage = 'readiness'
    $remainingSeconds = Get-InstallRemainingSeconds -Budget $budget
    if ($remainingSeconds -lt 1) { throw 'The install budget expired before the readiness check.' }
    if (-not (Wait-CodexCompanion -Exe $exe -TimeoutSeconds $remainingSeconds)) {
        throw "The companion did not become ready within the install budget ($InstallBudgetSeconds seconds). Check $RuntimeRoot\playback.log and retry install.cmd."
    }
} catch {
    # Roll back only a new entry and new helper processes from this directory.
    $installError = $_
    try {
        if ($null -eq $existing) {
            $current = Get-ItemProperty -Path $key -Name 'CodexBootAnimation' -ErrorAction SilentlyContinue
            if ($null -ne $current -and $current.CodexBootAnimation -eq $command) {
                Remove-ItemProperty -Path $key -Name 'CodexBootAnimation' -ErrorAction Stop
            }
        }
        Get-CodexCompanionProcesses -Exe $exe | Where-Object {
            $_.ProcessId -notin $before -and $_.CommandLine -match '(?:^|\s)--(?:supervise|watch|resident)(?:\s|$)'
        } | ForEach-Object { Stop-Process -Id $_.ProcessId -ErrorAction SilentlyContinue }
        if ($disabledExisted -and $disabledRemoved) {
            if (Test-Path -LiteralPath $disabled) {
                Write-Warning 'The pause flag was rewritten during installation; the newer file was left in place.'
            } else {
                [IO.File]::WriteAllBytes($disabled, $disabledBytes)
            }
        }
    } catch { Write-Warning 'Could not fully roll back startup. Check the tray and Windows Startup Apps before retrying.' }
    throw ("Install failed at stage '" + $stage + "': " + $installError.Exception.Message)
} finally {
    if ($installLocked) { try { $installMutex.ReleaseMutex() } catch { } }
    $installMutex.Dispose()
}
Write-Output "Registered the companion installation: $command"
if ($startup.ApprovalState -eq 'Disabled') { Write-Output 'Windows sign-in startup remains disabled; manual-launch hooks can still find this installation.' }
Write-Output 'This does not modify Codex itself. See docs/INSTALLATION.md for the plugin hook.'
