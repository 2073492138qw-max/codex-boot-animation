#Requires -Version 5.1
[CmdletBinding()]
param([string]$Exe)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-Support.ps1')
. (Join-Path $PSScriptRoot 'Test-Process.ps1')
if (-not $Exe) { $Exe = Join-Path (Split-Path -Parent $PSScriptRoot) 'plugins\codex-boot-animation\hooks\BootPlayer.exe' }
$fixture = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('codex-root-guard-' + [guid]::NewGuid().ToString('N'))))
$savedRoot = $env:CODEX_BOOT_ANIMATION_TEST_ROOT
$links = New-Object 'Collections.Generic.List[string]'
function Require([bool]$Value, [string]$Case) { if (-not $Value) { throw "Runtime isolation failed: $Case" } }
function Exit-Code([string[]]$Arguments) {
    $child = Start-TestProcess -FilePath $Exe -ArgumentList $Arguments -Wait -PassThru -TimeoutSeconds 10
    try { return $child.ExitCode } finally { $child.Dispose() }
}
function Reject-Root([string]$Path, [string]$Case) {
    Require ((Exit-Code @('--test-root', $Path, '--test-fail')) -eq 46) "$Case executable"
    $refused = $false
    try { $null = Resolve-IsolatedRuntimeRoot -RuntimeRoot $Path } catch { $refused = $true }
    Require $refused "$Case installer"
    Write-Output "PASS root-guard-$Case"
}
try {
    $env:CODEX_BOOT_ANIMATION_TEST_ROOT = $null
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    # This safe mode exits immediately even in the preceding build. Fail here
    # before trying paths if a stale executable lacks the new guard.
    Require ((Exit-Code @('--test-fail')) -eq 46) 'unbound-fixture'
    foreach ($mode in @('--intro-media-decoder-probe', '--repair-log-child', '--lifetime-parent', '--lifetime-probe', '--test-hang', '--button-probe-hang-test')) {
        Require ((Exit-Code @($mode)) -eq 46) "unbound $mode"
    }
    Write-Output 'PASS root-guard-unbound-helper-modes'
    $real = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation'
    Reject-Root $real 'real-root'
    Reject-Root (Join-Path $real 'guard-must-not-create') 'real-child'
    Reject-Root (Split-Path -Parent $real) 'real-parent'
    Reject-Root ([IO.Path]::GetPathRoot($fixture)) 'drive-root'
    Reject-Root 'guard-relative-must-not-create' 'relative'
    Reject-Root ($fixture.Substring(0,2) + 'guard-relative-must-not-create') 'drive-relative'
    Reject-Root (Join-Path $fixture 'ambiguous.') 'trailing-dot'

    $target = Join-Path $fixture 'protected-target'
    [IO.Directory]::CreateDirectory($target) | Out-Null
    $sentinel = Join-Path $target 'disabled'
    [IO.File]::WriteAllText($sentinel, 'fixture-state-must-survive')
    $before = (Get-FileHash -LiteralPath $sentinel).Hash
    $alias = Join-Path $fixture 'alias'
    New-Item -ItemType Junction -Path $alias -Target $target | Out-Null
    $links.Add($alias)
    Reject-Root $alias 'junction-root'
    Reject-Root (Join-Path $alias 'missing-child') 'junction-ancestor'
    $nested = Join-Path $fixture 'nested'
    [IO.Directory]::CreateDirectory($nested) | Out-Null
    $nestedLink = Join-Path $nested 'turns'
    New-Item -ItemType Junction -Path $nestedLink -Target $target | Out-Null
    $links.Add($nestedLink)
    Reject-Root $nested 'junction-child'
    Require ((Get-FileHash -LiteralPath $sentinel).Hash -eq $before) 'junction target changed'
    Require (@(Get-ChildItem -LiteralPath $target -Force).Count -eq 1) 'junction target gained files'

    $valid = Join-Path $fixture 'valid root'
    Require ((Exit-Code @('--test-root', $valid, '--new-dialog-parser-test')) -eq 0) 'valid root rejected'
    Require (Test-Path -LiteralPath (Join-Path $valid 'playback.log')) 'log escaped isolated root'
    $env:CODEX_BOOT_ANIMATION_TEST_ROOT = $valid
    Require ((Exit-Code @('--test-fail')) -eq 47) 'child did not inherit valid binding'
    $env:CODEX_BOOT_ANIMATION_TEST_ROOT = $alias
    Require ((Exit-Code @('--test-fail')) -eq 46) 'inherited alias accepted'
    Write-Output 'PASS root-guard-valid-write-and-child-inheritance'
} finally {
    $env:CODEX_BOOT_ANIMATION_TEST_ROOT = $savedRoot
    # Delete only junction entries, never recurse through their targets.
    foreach ($link in $links) { if ([IO.Directory]::Exists($link)) { [IO.Directory]::Delete($link, $false) } }
    $full = [IO.Path]::GetFullPath($fixture)
    $temp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $full.StartsWith($temp, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $full) -notlike 'codex-root-guard-*') {
        throw 'Refusing to clean an unexpected fixture path.'
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
