#Requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$savedSandboxFamily = $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('codex-startup-policy-' + [guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixture)
$fixtureState = @{ Run = $null; Approval = [byte[]]@(3,0,0,0,0,0,0,0,0,0,0,0); Deny = $false }
function Assert-Startup([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-ItemProperty {
    param($Path, $Name, $ErrorAction)
    if ($Path -like '*StartupApproved*') {
        if ($fixtureState.Deny) { throw 'Fixture registry access denied.' }
        return [pscustomobject]@{ CodexBootAnimation = $fixtureState.Approval }
    }
    if ($Path -eq 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run') {
        return [pscustomobject]@{ CodexBootAnimation = $fixtureState.Run }
    }
    throw 'Unexpected registry read in startup fixture.'
}
function Test-Path {
    param($LiteralPath, $Path, $PathType, $ErrorAction)
    if ($LiteralPath -like 'HKCU:*') { return $true }
    $argsForTest = @{}
    if ($LiteralPath) { $argsForTest.LiteralPath = $LiteralPath } else { $argsForTest.Path = $Path }
    if ($PathType) { $argsForTest.PathType = $PathType }
    Microsoft.PowerShell.Management\Test-Path @argsForTest
}
# Any unexpected side effect fails before it reaches Windows.
function Start-Process { throw 'Unexpected process launch in startup fixture.' }
function Stop-Process { throw 'Unexpected process stop in startup fixture.' }
function New-ItemProperty { throw 'Unexpected registry write in startup fixture.' }
function Remove-ItemProperty { throw 'Unexpected registry removal in startup fixture.' }
try {
    . (Join-Path $PSScriptRoot 'Install-Support.ps1')
    $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY = 'startup-registry-test-fixture'
    $rejected = $false
    try { $null = Get-CompanionStartupStatus -RuntimeRoot $fixture } catch { $rejected = $_.Exception.Message -like 'An isolated Codex registry view*' }
    Assert-Startup $rejected 'Packaged registry view was reported as physical Windows startup state.'
    # Windows operations are intercepted by this fixture. Clear the marker only
    # inside this test process to exercise the independent-shell code path.
    $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY = $null
    Write-Output 'PASS isolated-registry-view-cannot-claim-startup-disabled'
    $scripts = Join-Path $fixture 'scripts'
    $source = Join-Path $fixture 'plugins\codex-boot-animation'
    $target = Join-Path $fixture 'installed'
    foreach ($dir in @($scripts, (Join-Path $source 'hooks'), (Join-Path $target 'hooks'), (Join-Path $target '.codex-plugin'))) {
        $null = [IO.Directory]::CreateDirectory($dir)
    }
    foreach ($name in @('Install-Support.ps1','Update-Companion.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $scripts $name)
    }
    [IO.File]::WriteAllText((Join-Path $scripts 'Test.ps1'), "throw 'FIXTURE_PREFLIGHT_PASSED'")
    [IO.File]::WriteAllText((Join-Path $target '.codex-plugin\plugin.json'), '{"name":"codex-boot-animation"}')
    foreach ($root in @($source,$target)) {
        [IO.File]::WriteAllText((Join-Path $root 'hooks\hooks.json'), '{}')
        [IO.File]::WriteAllText((Join-Path $root 'hooks\BootPlayer.exe'), 'inert fixture; never execute')
    }
    $fixtureState.Run = '"' + (Join-Path $target 'hooks\BootPlayer.exe') + '" --supervise'
    $failure = ''
    try { & (Join-Path $scripts 'Update-Companion.ps1') -PluginRoot $target -KeepStartupDisabled } catch { $failure = $_.Exception.Message }
    Assert-Startup ($failure -eq 'FIXTURE_PREFLIGHT_PASSED') ('Disabled registered installation was rejected: ' + $failure)
    Write-Output 'PASS updater-accepts-disabled-registration-without-mutating-it'
    $registered = $fixtureState.Run
    foreach ($approval in @($null, [byte[]]@(2,0,0,0,0,0,0,0,0,0,0,0), [byte[]]@(3,0), [byte[]]@(99,0,0,0,0,0,0,0,0,0,0,0), '3')) {
        $fixtureState.Approval = $approval
        $failure = ''
        try { & (Join-Path $scripts 'Update-Companion.ps1') -PluginRoot $target -KeepStartupDisabled } catch { $failure = $_.Exception.Message }
        Assert-Startup ($failure -like 'Sign-in startup is not verified disabled*') 'Updater accepted missing, enabled or malformed startup approval.'
    }
    $fixtureState.Approval = [byte[]]@(3,0,0,0,0,0,0,0,0,0,0,0)
    $fixtureState.Run = '"C:\unrelated\BootPlayer.exe" --supervise'
    $failure = ''
    try { & (Join-Path $scripts 'Update-Companion.ps1') -PluginRoot $target -KeepStartupDisabled } catch { $failure = $_.Exception.Message }
    Assert-Startup ($failure -like 'Target does not match*') 'Updater accepted another registered installation.'
    $fixtureState.Run = $null
    $failure = ''
    try { & (Join-Path $scripts 'Update-Companion.ps1') -PluginRoot $target -KeepStartupDisabled } catch { $failure = $_.Exception.Message }
    Assert-Startup ($failure -eq 'FIXTURE_PREFLIGHT_PASSED') 'Explicitly unregistered safety update regressed.'
    $fixtureState.Run = $registered
    $fixtureState.Deny = $true
    $failure = ''
    try { & (Join-Path $scripts 'Update-Companion.ps1') -PluginRoot $target -KeepStartupDisabled } catch { $failure = $_.Exception.Message }
    Assert-Startup ($failure -eq 'Fixture registry access denied.') 'Unreadable approval was treated as permission to continue.'
    $fixtureState.Deny = $false
    Write-Output 'PASS updater-refuses-unsafe-unknown-and-foreign-startup-state'

    . (Join-Path $PSScriptRoot 'Install-Support.ps1')
    $policyFile = Join-Path $fixture 'startup-policy.json'
    foreach ($invalid in @('{broken', '{}', 'null', '[]', '{"AnimationSignInStartup":false}', '{"AnimationSignInStartup":"Unexpected"}')) {
        [IO.File]::WriteAllText($policyFile, $invalid)
        $rejected = $false
        try { $null = Get-CompanionStartupStatus -RuntimeRoot $fixture } catch { $rejected = $true }
        Assert-Startup $rejected 'Malformed policy was silently ignored.'
    }
    [IO.File]::WriteAllText($policyFile, '{"AnimationSignInStartup":"Disabled"}')
    $fixtureState.Approval = [byte[]]@(2,0,0,0,0,0,0,0,0,0,0,0)
    $status = Get-CompanionStartupStatus -RuntimeRoot $fixture
    $rejected = $false
    try { Assert-CompanionStartupPolicy -Status $status -RegisteredCommand $registered } catch { $rejected = $true }
    Assert-Startup $rejected 'Saved manual-only preference needed an extra command-line switch to take effect.'
    Assert-CompanionStartupPolicy -Status $status -RegisteredCommand ''
    [IO.File]::WriteAllText($policyFile, '{"AnimationSignInStartup":"Enabled"}')
    Assert-CompanionStartupPolicy -Status (Get-CompanionStartupStatus -RuntimeRoot $fixture) -RegisteredCommand $registered
    [IO.File]::WriteAllText($policyFile, '{"CodexStartup":"DisabledByUser","AnimationSignInStartup":"Enabled","AnimationScheduledTask":"Disabled"}')
    $splitStatus = Get-CompanionStartupStatus -RuntimeRoot $fixture
    Assert-Startup (-not $splitStatus.ManualOnly -and $splitStatus.ApprovalState -eq 'Enabled') 'Codex manual-only preference incorrectly disabled the animation companion.'
    Assert-CompanionStartupPolicy -Status $splitStatus -RegisteredCommand $registered
    Write-Output 'PASS codex-manual-only-with-companion-autostart'
    Remove-Item -LiteralPath $policyFile
    Assert-CompanionStartupPolicy -Status (Get-CompanionStartupStatus -RuntimeRoot $fixture) -RegisteredCommand $registered
    Write-Output 'PASS startup-policy-precedence-and-fail-closed-reading'
} finally {
    $env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY = $savedSandboxFamily
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notlike 'codex-startup-policy-*') { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
