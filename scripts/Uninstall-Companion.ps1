#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory = $true)][string]$PluginRoot)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $PluginRoot).Path
$manifest = Get-Content -LiteralPath (Join-Path $root '.codex-plugin\plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.name -ne 'codex-boot-animation') { throw 'Not a Codex Boot Animation installation.' }
$exe = [IO.Path]::GetFullPath((Join-Path $root 'hooks\BootPlayer.exe'))
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$value = (Get-ItemProperty -LiteralPath $key -Name CodexBootAnimation -ErrorAction SilentlyContinue).CodexBootAnimation
$accepted = @(('"' + $exe + '" --supervise'), ('"' + $exe + '" --watch'))
if ($value -and $value -notin $accepted) { throw 'Startup belongs to another installation; nothing changed.' }
$tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object {
    $_.TaskName -like 'CodexBootAnimation*' -and
    @($_.Actions | Where-Object { $_.Execute -ieq $exe }).Count -gt 0 -and
    @($_.Actions | Where-Object { $_.Execute -ine $exe }).Count -eq 0
})
if (-not $PSCmdlet.ShouldProcess($root, 'Disable companion hooks, remove its startup registrations and stop its exact executable')) { return }
# Hooks can remain registered in Codex: this flag prevents their next call from reviving the helper.
$data = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation'
New-Item -ItemType Directory -Path $data -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'disabled'), 'disabled')
if ($value) { Remove-ItemProperty -LiteralPath $key -Name CodexBootAnimation }
foreach ($task in $tasks) { Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath $task.TaskPath -Confirm:$false }
# Stop supervisors first, then their children; never stop another same-name application.
foreach ($mode in @('--supervise', '--watch', '--resident', 'remaining')) {
    $owned = @(Get-CimInstance Win32_Process -Filter "Name='BootPlayer.exe'" | Where-Object {
        $_.ExecutablePath -ieq $exe -and ($mode -eq 'remaining' -or $_.CommandLine -match [regex]::Escape($mode))
    })
    foreach ($process in $owned) {
        $child = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
        if ($child) {
            try { $child.Kill(); if (-not $child.WaitForExit(5000)) { throw 'Companion did not stop.' } }
            finally { $child.Dispose() }
        }
    }
}
if ((Get-ItemProperty -LiteralPath $key -Name CodexBootAnimation -ErrorAction SilentlyContinue).CodexBootAnimation) { throw 'Startup entry still exists.' }
Write-Output 'Companion disabled and stopped. Personal videos/settings are retained. Remove the plugin in Codex to remove its catalog entry.'
