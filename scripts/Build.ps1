#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repo 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw 'No C# source files were found.' }
$outputDir = Join-Path $repo 'build'
$output = Join-Path $outputDir 'BootPlayer.exe'
$pluginExe = Join-Path $repo 'plugins\codex-boot-animation\hooks\BootPlayer.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework 4 C# compiler is required on Windows.'
}

$references = @(
    'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Management.dll',
    'System.Windows.Forms.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll',
    'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll',
    'WPF\PresentationFramework.dll', 'WPF\UIAutomationClient.dll',
    'WPF\UIAutomationTypes.dll'
)
$arguments = @('/nologo', '/target:winexe', "/out:$output")
$trayIcon = Join-Path $repo 'assets\tray-icon.ico'
if (-not (Test-Path -LiteralPath $trayIcon -PathType Leaf)) { throw 'The embedded tray icon asset is missing.' }
# Managed resource only: do not change the executable or Codex window icons.
$arguments += "/resource:$trayIcon,CodexBootAnimation.TrayIcon"
foreach ($reference in $references) {
    $path = Join-Path $framework $reference
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing framework assembly: $path" }
    $arguments += "/reference:$path"
}
$arguments += $sources
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE" }
Copy-Item -LiteralPath $output -Destination $pluginExe -Force
Write-Output "Built: $pluginExe"
