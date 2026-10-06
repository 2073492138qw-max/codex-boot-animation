#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$savedSandboxFamily = $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
function Assert-Install([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('codex-install-tests-' + [guid]::NewGuid().ToString('N'))
$fixtureRoot = [IO.Path]::GetFullPath($fixtureRoot)
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
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
    $quotedOutput = Invoke-CodexInstallerCommand $nativeShell @('-NoProfile', '-Command', "Write-Output 'path with spaces\'; Write-Output 'literal `"quote`"'")
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
    function Start-Process {
        param($FilePath, $ArgumentList, $WindowStyle, [switch]$Wait, [switch]$PassThru)
        if ($ArgumentList -eq '--adopt-running') { return [pscustomobject]@{ ExitCode = [int]$fixtureState.failAdopt } }
        if ($ArgumentList -eq '--supervise') {
            $fixtureState.processes += [pscustomobject]@{ ProcessId = 999; ExecutablePath = $FilePath; CommandLine = 'BootPlayer.exe --supervise' }
            if ($fixtureState.failLaunch) { throw 'Fixture launch failure.' }
            return
        }
        return [pscustomobject]@{ ExitCode = $fixtureState.probeExit }
    }
    function Get-ItemProperty {
        param($Path, $Name, $ErrorAction)
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
    Assert-Install (-not (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 0)) 'Accepted a missing resident player.'
    Set-FixtureProcesses @('--supervise', '--watch', '--resident')
    $fixtureState.probeExit = 15
    Assert-Install (-not (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 0)) 'Accepted an unresponsive pipe.'
    $fixtureState.probeExit = 0
    Assert-Install (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 0) 'Rejected a healthy companion.'
    foreach ($process in $fixtureState.processes) { $process.ExecutablePath = 'C:\unrelated\BootPlayer.exe' }
    Assert-Install (-not (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 0)) 'Accepted processes from a different installation.'
    Set-FixtureProcesses @('--supervise', '--watch', '--resident')
    $fixtureState.delayed = $true
    $fixtureState.processReads = 0
    Assert-Install (Wait-CodexCompanion -Exe $fixtureState.testExe -TimeoutSeconds 2) 'Did not wait for slow startup.'
    Assert-Install ($fixtureState.processReads -ge 2) 'Slow startup fixture did not retry.'
    $fixtureState.delayed = $false
    Write-Output 'PASS installer-companion-readiness-and-retry'

    $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY = 'installation-test-fixture'
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') | Out-Null } catch { $rejected = $true }
    Assert-Install ($rejected -and $null -eq $fixtureState.runValue) 'Did not reject startup registration in a packaged shell.'
    # Every registry/process operation above is stubbed. Only this test process
    # drops the flag to exercise the transaction, then restores it in finally.
    $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY = $null
    Set-FixtureProcesses @('--watch', '--resident')
    & (Join-Path $PSScriptRoot 'Install-Startup.ps1') | Out-Null
    Assert-Install ($fixtureState.runValue -eq ('"' + $fixtureState.testExe + '" --supervise')) 'Healthy install did not save startup.'
    Assert-Install (-not $fixtureState.removed -and $fixtureState.stopped.Count -eq 0) 'Healthy install rolled back.'
    $fixtureState.runValue = $null
    $fixtureState.failLaunch = $true
    $fixtureState.removed = $false
    $fixtureState.stopped = @()
    Set-FixtureProcesses @('--watch')
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') | Out-Null } catch { $rejected = $true }
    Assert-Install $rejected 'Startup launch failure was not reported.'
    Assert-Install ($fixtureState.removed -and $null -eq $fixtureState.runValue) 'New startup entry was not rolled back.'
    Assert-Install ($fixtureState.stopped.Count -eq 1 -and $fixtureState.stopped[0] -eq 999) 'Rollback affected an existing helper.'
    $fixtureState.failLaunch = $false
    $fixtureState.failAdopt = $true
    $fixtureState.removed = $false
    $fixtureState.runValue = '"' + $fixtureState.testExe + '" --supervise'
    $savedRun = $fixtureState.runValue
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Install-Startup.ps1') | Out-Null } catch { $rejected = $true }
    Assert-Install ($rejected -and -not $fixtureState.removed -and $fixtureState.runValue -eq $savedRun) 'Rollback removed a preexisting startup entry.'
    Write-Output 'PASS installer-startup-success-and-rollback'
} finally {
    $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY = $savedSandboxFamily
    # The fixture contains only our named CMD files; no recursive delete.
    foreach ($name in @('desktop cli.cmd', 'old cli.cmd', 'bad json.cmd')) {
        $fixturePath = Join-Path $fixtureRoot $name
        if (Test-Path -LiteralPath $fixturePath) { Remove-Item -LiteralPath $fixturePath }
    }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot }
}
