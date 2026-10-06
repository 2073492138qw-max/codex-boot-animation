#Requires -Version 5.1
# Shared installation checks. Loading this file does not change any settings.
function Invoke-CodexInstallerCommand {
    param([string]$CliPath, [string[]]$Arguments, [int]$TimeoutSeconds = 15)
    if ([IO.Path]::GetExtension($CliPath) -ne '.exe') {
        # Non-native fixtures are used only by the explicit candidate unit tests.
        $result = & $CliPath @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Codex command failed.' }
        return ($result -join [Environment]::NewLine)
    }
    $quoted = @($Arguments | ForEach-Object {
        $escaped = [regex]::Replace($_, '(\\*)"', '$1$1\"')
        $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
        '"' + $escaped + '"'
    })
    $start = New-Object Diagnostics.ProcessStartInfo($CliPath, ($quoted -join ' '))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = [Text.Encoding]::UTF8
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            $null = $process.WaitForExit(1000)
            throw "Codex command timed out after $TimeoutSeconds seconds. Check Codex/network availability, then retry."
        }
        if ($process.ExitCode -ne 0) { throw 'Codex command failed. Check Codex/plugin policy and retry.' }
        return $stdout.Result.TrimEnd()
    } finally { $process.Dispose() }
}

function Resolve-CodexInstallerCli {
    [CmdletBinding()]
    param([string[]]$Candidates)
    if (-not $PSBoundParameters.ContainsKey('Candidates')) {
        # Prefer the CLI shipped with desktop over an unrelated/older PATH install.
        $binRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'OpenAI\Codex\bin'
        $Candidates = @(Get-ChildItem -LiteralPath $binRoot -Filter 'codex.exe' -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | ForEach-Object { $_.FullName })
        $pathCommand = Get-Command codex -ErrorAction SilentlyContinue
        if ($null -ne $pathCommand -and $pathCommand.Source -and [IO.Path]::GetExtension($pathCommand.Source) -eq '.exe') { $Candidates += $pathCommand.Source }
    }
    $failures = @()
    foreach ($candidate in @($Candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        try {
            $version = Invoke-CodexInstallerCommand $candidate @('--version')
            $null = Invoke-CodexInstallerCommand $candidate @('plugin', 'add', '--help')
            $null = Invoke-CodexInstallerCommand $candidate @('plugin', 'marketplace', 'add', '--help')
            $null = Invoke-CodexInstallerCommand $candidate @('plugin', 'list', '--help')
            $raw = Invoke-CodexInstallerCommand $candidate @('plugin', 'marketplace', 'list', '--json')
            $sources = ($raw -join [Environment]::NewLine) | ConvertFrom-Json -ErrorAction Stop
            if ($null -eq $sources -or $sources.PSObject.Properties.Name -notcontains 'marketplaces') {
                throw 'Unexpected marketplace response from this CLI.'
            }
            return [pscustomobject]@{ Path = $candidate; Version = ($version -join ' '); Sources = $sources }
        } catch { $failures += "$candidate : $($_.Exception.Message)" }
    }
    $detail = if ($failures.Count) { [Environment]::NewLine + ($failures -join [Environment]::NewLine) } else { '' }
    throw "No compatible Codex CLI was found. Open/update Codex desktop, then retry install.cmd. Plugin and startup settings were not changed.$detail"
}

function Get-CodexCompanionProcesses {
    param([string]$Exe)
    @(Get-CimInstance Win32_Process -Filter "name='BootPlayer.exe'" -ErrorAction Stop |
        Where-Object { $_.ExecutablePath -ieq $Exe })
}

function Get-CodexBootHooks {
    param([string]$CliPath, [string]$Cwd, [int]$TimeoutSeconds = 8, [switch]$IncludeEntries)
    # hooks/list is read-only. Never call a trust/update method from diagnostics.
    if ([IO.Path]::GetExtension($CliPath) -ne '.exe') { throw 'Hook inspection requires the native desktop CLI.' }
    $start = New-Object Diagnostics.ProcessStartInfo($CliPath, 'app-server --stdio')
    $start.WorkingDirectory = $Cwd
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $stderr = $process.StandardError.ReadToEndAsync()
    # .NET Framework's redirected stdin can use a legacy Windows code page.
    # RPC JSON must be UTF-8 even when the installation cwd contains Chinese.
    $rpcWriter = New-Object IO.StreamWriter -ArgumentList $process.StandardInput.BaseStream, (New-Object Text.UTF8Encoding($false))
    $rpcWriter.AutoFlush = $true
    function Read-BootRpc($Id) {
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
            $line = $process.StandardOutput.ReadLineAsync()
            $remaining = [Math]::Max(1, [int](($TimeoutSeconds - $timer.Elapsed.TotalSeconds) * 1000))
            if (-not $line.Wait($remaining)) { throw 'Hook inspection timed out.' }
            if ($null -eq $line.Result) { throw 'Hook inspection process exited.' }
            $reply = $line.Result | ConvertFrom-Json -ErrorAction Stop
            if ($reply.id -eq $Id) {
                if ($reply.error) { throw 'This Codex version could not inspect hook state.' }
                return $reply.result
            }
        }
        throw 'Hook inspection timed out.'
    }
    try {
        $initialize = @{ id = 1; method = 'initialize'; params = @{
            clientInfo = @{ name = 'codex_boot_diagnostics'; version = '1.0.0' }
            capabilities = @{ experimentalApi = $true; explicitGatewayOauth = $true }
        } }
        $rpcWriter.WriteLine(($initialize | ConvertTo-Json -Depth 6 -Compress))
        $null = Read-BootRpc 1
        $rpcWriter.WriteLine('{"method":"initialized"}')
        # Populate the local plugin catalog before inspecting plugin hooks.
        $catalogRequest = @{ id = 2; method = 'plugin/list'; params = @{ cwds = @($Cwd); forceRefetch = $false; marketplaceKinds = @('local') } }
        $rpcWriter.WriteLine(($catalogRequest | ConvertTo-Json -Depth 4 -Compress))
        $null = Read-BootRpc 2
        $request = @{ id = 3; method = 'hooks/list'; params = @{ cwds = @($Cwd) } }
        $rpcWriter.WriteLine(($request | ConvertTo-Json -Depth 4 -Compress))
        $response = Read-BootRpc 3
        if ($IncludeEntries) { return @($response.data) }
        return @($response.data | ForEach-Object { $_.hooks } | Where-Object {
            $_.pluginId -like 'codex-boot-animation@*'
        })
    } finally {
        $rpcWriter.Dispose()
        if (-not $process.WaitForExit(1000)) { $process.Kill() }
        $process.Dispose()
    }
}

function Wait-CodexCompanion {
    param([string]$Exe, [int]$TimeoutSeconds = 20)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $processes = @(Get-CodexCompanionProcesses -Exe $Exe)
        $ready = $true
        foreach ($mode in @('--supervise', '--watch', '--resident')) {
            if (-not @($processes | Where-Object { $_.CommandLine -match ('(?:^|\s)' + $mode + '(?:\s|$)') }).Count) {
                $ready = $false
                break
            }
        }
        if ($ready) {
            # Being alive now is insufficient: an attached helper dies with Codex.
            $supervisor = $processes | Where-Object { $_.CommandLine -match '(?:^|\s)--supervise(?:\s|$)' } | Select-Object -First 1
            $lifetime = Start-Process -FilePath $Exe -ArgumentList @('--lifetime-check', $supervisor.ProcessId) -WindowStyle Hidden -Wait -PassThru
            if ($lifetime.ExitCode -ne 0) { $ready = $false }
        }
        if ($ready) {
            # An invalid idle token only checks IPC; it never requests playback.
            $probe = Start-Process -FilePath $Exe -ArgumentList '--idle-pipe-probe' -WindowStyle Hidden -Wait -PassThru
            if ($probe.ExitCode -eq 0) { return $true }
        }
        if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) { break }
        Start-Sleep -Milliseconds 300
    } while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds)
    return $false
}
