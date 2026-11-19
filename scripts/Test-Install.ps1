#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$savedSandboxFamily = $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
function Assert-Install([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Get-RealRunValue {
    # Read the real Run entry directly: the fixture below replaces Get-ItemProperty.
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run')
    try { if ($key) { return $key.GetValue('CodexBootAnimation') } else { return $null } }
    finally { if ($key) { $key.Dispose() } }
}
# Read-only snapshot of the real profile. Installing from this fixture must not
# change the pause flag, the Run entry or the user's own settings; no chat
# content is read or written.
$realRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation'
function Get-ProtectedState {
    $state = [ordered]@{ RunEntry = Get-RealRunValue }
    $approvalKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run')
    try {
        $approval = if ($approvalKey) { $approvalKey.GetValue('CodexBootAnimation') } else { $null }
        $state.StartupApproval = if ($approval -is [byte[]]) { [BitConverter]::ToString($approval) } else { [string]$approval }
    } finally { if ($approvalKey) { $approvalKey.Dispose() } }
    foreach ($name in @('disabled', 'paused', 'scene-settings.json', 'completion-layout.json', 'startup-policy.json')) {
        $file = Join-Path $realRoot $name
        $state[$name] = if (Test-Path -LiteralPath $file -PathType Leaf) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } else { '<absent>' }
    }
    return $state
}
function Assert-ProtectedState($Before) {
    $after = Get-ProtectedState
    foreach ($name in $Before.Keys) {
        if ($Before[$name] -ne $after[$name]) { throw "A test changed real user state: $name" }
    }
}
$protectedBefore = Get-ProtectedState
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('codex-install-tests-' + [guid]::NewGuid().ToString('N'))
$fixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$stateFailure = $null
try {
    $good = Join-Path $fixtureRoot 'desktop cli.cmd'
    $old = Join-Path $fixtureRoot 'old cli.cmd'
    $badJson = Join-Path $fixtureRoot 'bad json.cmd'
    $fixture = @'
@echo off
if "%1"=="--version" (
    echo codex-cli fixture
    exit /b 0
)
if "%4"=="--help" exit /b 0
if "%3"=="--help" exit /b 0
echo {"marketplaces":[]}
exit /b 0
'@
    [IO.File]::WriteAllText($good, $fixture, [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($old, "@echo off`r`nexit /b 1`r`n", [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText($badJson, $fixture.Replace('echo {"marketplaces":[]}', 'echo invalid-json'), [Text.Encoding]::ASCII)
    $resolved = Resolve-CodexInstallerCli -Candidates @($good, $old)
    Assert-Install ($resolved.Path -eq $good) 'Did not prefer the desktop CLI.'
    $resolved = Resolve-CodexInstallerCli -Candidates @($old, $badJson, $good)
    Assert-Install ($resolved.Path -eq $good) 'Did not recover from incompatible CLI candidates.'
    $rejected = $false
    try { $null = Resolve-CodexInstallerCli -Candidates @($old, $badJson) } catch { $rejected = $true }
    Assert-Install $rejected 'Accepted incompatible CLI candidates.'
    $rejected = $false
    try { $null = Resolve-CodexInstallerCli -Candidates @() } catch { $rejected = $true }
    Assert-Install $rejected 'Accepted a missing CLI.'
    $nativeShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $quotedOutput = Invoke-CodexInstallerCommand $nativeShell @('-NoProfile', '-Command', "Write-Output 'path with spaces\\'; Write-Output 'literal `"quote`"'")
    Assert-Install ($quotedOutput -match 'path with spaces\\' -and $quotedOutput -match 'literal "quote"') 'Native argument quoting failed.'
    $rejected = $false
    $timeoutTimer = [Diagnostics.Stopwatch]::StartNew()
    try { $null = Invoke-CodexInstallerCommand $nativeShell @('-NoProfile', '-Command', 'Start-Sleep -Seconds 5') -TimeoutSeconds 1 } catch { $rejected = $_.Exception.Message -like '*timed out*' }
    Assert-Install ($rejected -and $timeoutTimer.Elapsed.TotalSeconds -lt 4) 'A hanging native command did not time out.'
    Write-Output 'PASS installer-cli-selection-and-fallback'

    # Stub external Windows operations, then exercise the production readiness
    # check and startup transaction without changing the real registry/processes.
    $repo = Split-Path -Parent $PSScriptRoot
    $fixtureState = @{}
    $fixtureState.testExe = Join-Path $repo 'plugins\codex-boot-animation\hooks\BootPlayer.exe'
    $fixtureState.processes = @()
    $fixtureState.probeExit = 0
    $fixtureState.processReads = 0
    $fixtureState.delayed = $false
    $fixtureState.failAdopt = $false
    $fixtureState.adoptHangs = $false
    $fixtureState.probeHangs = $false
    $fixtureState.killed = 0
    $fixtureState.failLaunch = $false
    $fixtureState.runValue = $null
    $fixtureState.removed = $false
    $fixtureState.stopped = @()
    function Get-CimInstance {
        param($ClassName, $Filter, $ErrorAction)
        $fixtureState.processReads++
        if ($fixtureState.delayed -and $fixtureState.processReads -eq 1) { return $fixtureState.processes[0] }
        return $fixtureState.processes
    }
    function Start-Sleep { param($Milliseconds) }
    function New-FixtureProcess([int]$Code, [bool]$Hangs) {
        $fake = [pscustomobject]@{ ExitCode = $Code; HasExited = (-not $Hangs); Hangs = $Hangs }
        $fake | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($Milliseconds) if ($this.Hangs) { return $false } else { return $true } }
        $fake | Add-Member -MemberType ScriptMethod -Name Kill -Value { $fixtureState.killed++ }
        $fake | Add-Member -MemberType ScriptMethod -Name Dispose -Value { }
        return $fake
    }
    function Start-Process {
        param($FilePath, $ArgumentList, $WindowStyle, [switch]$Wait, [switch]$PassThru)
        if ($ArgumentList -eq '--adopt-running') { return New-FixtureProcess ([int]$fixtureState.failAdopt) ([bool]$fixtureState.adoptHangs) }
        if ($ArgumentList -eq '--supervise') {
            $fixtureState.processes += [pscustomobject]@{ ProcessId = 999; ExecutablePath = $FilePath; CommandLine = 'BootPlayer.exe --supervise' }
            if ($fixtureState.failLaunch) { throw 'Fixture launch failure.' }
            return
        }
        if ($fixtureState.probeHangs) { return New-FixtureProcess 0 $true }
        return New-FixtureProcess ([int]$fixtureState.probeExit) $false
    }
    function Get-ItemProperty {
        param($Path, $Name, $ErrorAction)
        if ($Path -like '*StartupApproved*') { return [pscustomobject]@{ CodexBootAnimation = $fixtureState.approval } }
        if ($null -ne $fixtureState.runValue) { return [pscustomobject]@{ CodexBootAnimation = $fixtureState.runValue } }
    }
    function New-Item { param($Path, [switch]$Force) }
    function New-ItemProperty {
        param($Path, $Name, $Value, $PropertyType, [switch]$Force)
        $fixtureState.runValue = $Value
    }
    function Remove-ItemProperty {
        param($Path, $Name, $ErrorAction)
        $fixtureState.runValue = $null
        $fixtureState.removed = $true
    }
    function Stop-Process {
        param($Id, $ErrorAction)
        $fixtureState.stopped += $Id
    }
    function Set-FixtureProcesses([string[]]$Modes) {
        $fixtureState.processes = @()
        $id = 100
        foreach ($mode in $Modes) {
            $fixtureState.processes += [pscustomobject]@{ ProcessId = $id++; ExecutablePath = $fixtureState.testExe; CommandLine = 'BootPlayer.exe ' + $mode }
        }
    }
    Set-FixtureProcesses @('--supervise', '--watch')
    Assert-Install (-not (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 1)) 'Accepted a missing resident player.'
    Set-FixtureProcesses @('--supervise', '--watch', '--resident')
    $fixtureState.probeExit = 15
    Assert-Install (-not (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 1)) 'Accepted an unresponsive pipe.'
    $fixtureState.probeExit = 0
    Assert-Install (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 1) 'Rejected a healthy companion.'
    foreach ($process in $fixtureState.processes) { $process.ExecutablePath = 'C:\unrelated\BootPlayer.exe' }
    Assert-Install (-not (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 1)) 'Accepted processes from a different installation.'
    Set-FixtureProcesses @('--supervise', '--watch', '--resident')
    $fixtureState.delayed = $true
    $fixtureState.processReads = 0
    Assert-Install (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 2) 'Did not wait for slow startup.'
    Assert-Install ($fixtureState.processReads -ge 2) 'Slow startup fixture did not retry.'
    $fixtureState.delayed = $false
    # A hanging lifetime/pipe probe must not extend the caller's budget.
    $fixtureState.probeHangs = $true
    $probeTimer = [Diagnostics.Stopwatch]::StartNew()
    Assert-Install (-not (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 1)) 'Accepted a hanging pipe probe.'
    Assert-Install ($probeTimer.Elapsed.TotalSeconds -lt 4) 'A hanging probe exceeded the readiness budget.'
    $fixtureState.probeHangs = $false
    Write-Output 'PASS installer-hanging-probe-respects-budget'
    Write-Output 'PASS installer-companion-readiness-and-retry'

    # Installation receives an isolated runtime data root. A test must never be
    # able to point the transaction at the real %USERPROFILE% state.
    $runtimeRoot = Join-Path $fixtureRoot 'runtime'
    # New-Item is stubbed below, so create the fixture root directly.
    $null = [IO.Directory]::CreateDirectory($runtimeRoot)
    $startupArgs = @{ RuntimeRoot = $runtimeRoot; RequireIsolatedRuntimeRoot = $true }
    # The production entry must reject unsafe roots before any registry/process
    # operation, including roots that merely contain or sit below the real root.
    foreach ($unsafeRoot in @($realRoot, (Split-Path -Parent $realRoot), (Join-Path $realRoot 'guard-must-not-create'), 'relative-test-root')) {
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') -RuntimeRoot $unsafeRoot -RequireIsolatedRuntimeRoot | Out-Null } catch { $rejected = $true }
        Assert-Install ($rejected -and $null -eq $fixtureState.runValue) 'Unsafe runtime root reached the install transaction.'
    }
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') -RuntimeRoot $realRoot -RequireIsolatedRuntimeRoot | Out-Null } catch { $rejected = $true }
    Assert-Install ($rejected -and $null -eq $fixtureState.runValue) 'Accepted the real runtime root as a test root.'
    Write-Output 'PASS installer-rejects-real-runtime-root'

    $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY = 'installation-test-fixture'
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null } catch { $rejected = $true }
    Assert-Install ($rejected -and $null -eq $fixtureState.runValue) 'Did not reject startup registration in a packaged shell.'
    # Every registry/process operation above is stubbed. Only this test process
    # drops the flag to exercise the transaction, then restores it in finally.
    $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY = $null
    Set-FixtureProcesses @('--watch', '--resident')
    & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null
    Assert-Install ($fixtureState.runValue -eq ('"' + $fixtureState.testExe + '" --supervise')) 'Healthy install did not save startup.'
    Assert-Install (-not $fixtureState.removed -and $fixtureState.stopped.Count -eq 0) 'Healthy install rolled back.'
    $fixtureState.runValue = $null
    $fixtureState.failLaunch = $true
    $fixtureState.removed = $false
    $fixtureState.stopped = @()
    Set-FixtureProcesses @('--watch')
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null } catch { $rejected = $true }
    Assert-Install $rejected 'Startup launch failure was not reported.'
    Assert-Install ($fixtureState.removed -and $null -eq $fixtureState.runValue) 'New startup entry was not rolled back.'
    Assert-Install ($fixtureState.stopped.Count -eq 1 -and $fixtureState.stopped[0] -eq 999) 'Rollback affected an existing helper.'
    $fixtureState.failLaunch = $false
    $fixtureState.failAdopt = $true
    $fixtureState.removed = $false
    $fixtureState.runValue = '"' + $fixtureState.testExe + '" --supervise'
    $savedRun = $fixtureState.runValue
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null } catch { $rejected = $true }
    Assert-Install ($rejected -and -not $fixtureState.removed -and $fixtureState.runValue -eq $savedRun) 'Rollback removed a preexisting startup entry.'
    Write-Output 'PASS installer-startup-success-and-rollback'

    # The pause flag lives in the injected root and survives every failure path.
    $pauseFlag = Join-Path $runtimeRoot 'disabled'
    function Set-FixturePauseFlag([string]$Text) {
        [IO.File]::WriteAllText($pauseFlag, $Text, (New-Object Text.UTF8Encoding($false)))
    }
    function Assert-PauseFlagRestored([string]$Text, [string]$Case) {
        Assert-Install (Test-Path -LiteralPath $pauseFlag -PathType Leaf) "Pause flag was not restored after $Case."
        Assert-Install (([IO.File]::ReadAllText($pauseFlag)) -eq $Text) "Pause flag bytes changed after $Case."
    }
    $pauseText = 'paused-by-user'
    $fixtureState.failAdopt = $false
    $fixtureState.failLaunch = $false
    $fixtureState.probeExit = 0
    $fixtureState.runValue = $null
    $fixtureState.removed = $false
    $fixtureState.stopped = @()
    Set-FixtureProcesses @('--watch', '--resident')
    Set-FixturePauseFlag $pauseText
    & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null
    Assert-Install (-not (Test-Path -LiteralPath $pauseFlag)) 'Healthy install did not resume a paused user.'
    Write-Output 'PASS installer-healthy-install-clears-pause-flag'

    $fixtureState.runValue = $null
    $fixtureState.failAdopt = $true
    $fixtureState.removed = $false
    Set-FixturePauseFlag $pauseText
    Set-FixtureProcesses @('--watch', '--resident')
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null } catch { $rejected = $true }
    Assert-Install $rejected 'Adopt failure was not reported.'
    Assert-PauseFlagRestored $pauseText 'an adopt failure'
    $fixtureState.failAdopt = $false

    $fixtureState.failLaunch = $true
    Set-FixturePauseFlag $pauseText
    Set-FixtureProcesses @('--watch')
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null } catch { $rejected = $true }
    Assert-Install $rejected 'Launch failure was not reported.'
    Assert-PauseFlagRestored $pauseText 'a launch failure'
    $fixtureState.failLaunch = $false

    Set-FixturePauseFlag $pauseText
    Set-FixtureProcesses @('--watch', '--resident')
    $fixtureState.probeExit = 15
    $rejected = $false
    try {
        & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs -InstallBudgetSeconds 1 | Out-Null
        $rejected = $false
    } catch { $rejected = $true }
    Assert-Install $rejected 'Readiness failure was not reported.'
    Assert-PauseFlagRestored $pauseText 'a readiness failure'
    Write-Output 'PASS installer-pause-flag-transaction'

    # adopt never returns: the install must fail inside the budget, kill only the
    # child it started, and still restore the pause flag.
    $fixtureState.probeExit = 0
    $fixtureState.runValue = $null
    $fixtureState.removed = $false
    Set-FixtureProcesses @('--watch', '--resident')
    Set-FixturePauseFlag $pauseText
    $fixtureState.adoptHangs = $true
    $fixtureState.killed = 0
    $adoptTimer = [Diagnostics.Stopwatch]::StartNew()
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs -InstallBudgetSeconds 1 | Out-Null } catch { $rejected = $true }
    $fixtureState.adoptHangs = $false
    Assert-Install $rejected 'A hanging adopt was not reported.'
    Assert-Install ($adoptTimer.Elapsed.TotalSeconds -lt 6) 'A hanging adopt exceeded the install budget.'
    Assert-Install ($fixtureState.killed -eq 1) 'A hanging adopt did not terminate exactly the child it started.'
    Assert-PauseFlagRestored $pauseText 'an adopt timeout'
    Write-Output 'PASS installer-adopt-timeout-is-bounded'

    $policyFile = Join-Path $runtimeRoot 'startup-policy.json'
    [IO.File]::WriteAllText($policyFile, '{"AnimationSignInStartup":"Disabled"}')
    $fixtureState.failAdopt = $false
    $fixtureState.failLaunch = $false
    $fixtureState.probeExit = 0
    $fixtureState.runValue = $null
    # Guarantee that the mocked registry key exists even on clean CI accounts.
    function Test-Path {
        param($LiteralPath, $PathType, $ErrorAction)
        if ($LiteralPath -like 'HKCU:*StartupApproved*') { return $true }
        $parameters = @{ LiteralPath = $LiteralPath }
        if ($PathType) { $parameters.PathType = $PathType }
        Microsoft.PowerShell.Management\Test-Path @parameters
    }
    foreach ($approval in @($null, [byte[]]@(2,0,0,0,0,0,0,0,0,0,0,0), [byte[]]@(3,0))) {
        $fixtureState.approval = $approval
        $reads = $fixtureState.processReads
        $rejected = $false
        try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null } catch { $rejected = $_.Exception.Message -like 'Sign-in startup is not verified disabled*' }
        Assert-Install ($rejected -and $null -eq $fixtureState.runValue -and $reads -eq $fixtureState.processReads) 'Manual-only install reached external operations without verified disablement.'
    }
    $fixtureState.approval = [byte[]]@(3,0,0,0,1,2,3,4,5,6,7,8)
    $savedApproval = [BitConverter]::ToString($fixtureState.approval)
    Set-FixtureProcesses @('--watch', '--resident')
    & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null
    Assert-Install ($fixtureState.runValue -eq $savedRun -and [BitConverter]::ToString($fixtureState.approval) -eq $savedApproval) 'Manual-only install did not preserve disabled registration.'
    [IO.File]::WriteAllText($policyFile, '{broken')
    $reads = $fixtureState.processReads
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') @startupArgs | Out-Null } catch { $rejected = $true }
    Assert-Install ($rejected -and $reads -eq $fixtureState.processReads) 'A damaged policy allowed installation operations.'
    Write-Output 'PASS installer-manual-start-policy-and-corrupt-policy-refusal'
} finally {
    $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY = $savedSandboxFamily
    # Remove only paths this fixture created; never recurse into a computed path.
    foreach ($name in @('desktop cli.cmd', 'old cli.cmd', 'bad json.cmd')) {
        $fixturePath = Join-Path $fixtureRoot $name
        if (Test-Path -LiteralPath $fixturePath) { Remove-Item -LiteralPath $fixturePath }
    }
    $runtimeFixture = Join-Path $fixtureRoot 'runtime'
    foreach ($name in @('disabled', 'playback.log', 'startup-policy.json')) {
        $fixturePath = Join-Path $runtimeFixture $name
        if (Test-Path -LiteralPath $fixturePath) { Remove-Item -LiteralPath $fixturePath }
    }
    foreach ($directory in @($runtimeFixture, $fixtureRoot)) {
        if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory }
    }
    try { Assert-ProtectedState $protectedBefore } catch { $stateFailure = $_; Write-Output "FAIL real-profile-state-changed: $_" }
}
if ($stateFailure) { throw $stateFailure }
Write-Output 'PASS real-profile-state-unchanged'
