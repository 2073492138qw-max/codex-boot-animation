#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PluginRoot,
      [string[]]$Cases = @('--repair-regression-test','--resident-protocol-test','--navigation-log-test','--new-dialog-parser-test','--route-transition-test','--scene-settings-test','--completion-flow-test'),
      [ValidateRange(1,120)][int]$TimeoutSeconds=30)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path -LiteralPath $PluginRoot).Path 'hooks\BootPlayer.exe'
# Installed-package tests write only to an isolated runtime root.
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('codex-installed-tests-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$failed=0
try {
    foreach ($case in $Cases) {
        $child=$null
        try {
            $child=Start-Process -FilePath $exe -ArgumentList ('--test-root "'+$testRoot+'" '+$case) -WindowStyle Hidden -PassThru
            if (-not $child.WaitForExit($TimeoutSeconds*1000)) {
                $child.Kill(); $child.WaitForExit(5000) | Out-Null
                throw "Timeout: $case"
            }
            if ($child.ExitCode -ne 0) { throw "Exit $($child.ExitCode): $case" }
            Write-Output "PASS $case"
        } catch { $failed++; Write-Output "FAIL $_" }
        finally { if ($child) { $child.Dispose() } }
    }
} finally {
    if ((Test-Path -LiteralPath $testRoot) -and (Split-Path -Leaf $testRoot) -like 'codex-installed-tests-*') {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
Write-Output "$($Cases.Count-$failed)/$($Cases.Count) passed"
if ($failed) { exit 1 }
exit 0
