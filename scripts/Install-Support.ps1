#Requires -Version 5.1
# Shared installation checks. Loading this file does not change any settings.
function Get-CompanionStartupStatus {
    param([string]$RuntimeRoot)
    if ($env:CODEX_WINDOWS_SANDBOX_PACKAGE_FAMILY) {
        throw 'An isolated Codex registry view cannot verify Windows sign-in startup. Run this check from independent Windows PowerShell.'
    }
    if (-not $RuntimeRoot) { $RuntimeRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation' }
    $policyPath = Join-Path $RuntimeRoot 'startup-policy.json'
    $manualOnly = $false
    if (Test-Path -LiteralPath $policyPath) {
        # A damaged preference must not silently turn into permission to autostart.
        $policy = Get-Content -LiteralPath $policyPath -Raw -Encoding UTF8 -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ($null -eq $policy -or $policy.AnimationSignInStartup -isnot [string] -or $policy.AnimationSignInStartup -cnotin @('Disabled','Enabled')) {
            throw 'Invalid startup-policy.json; startup preference could not be verified. Nothing changed.'
        }
        $manualOnly = $policy.AnimationSignInStartup -ceq 'Disabled'
    }
    $approvalKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
    $approval = $null
    if (Test-Path -LiteralPath $approvalKey -ErrorAction Stop) {
        $approval = (Get-ItemProperty -Path $approvalKey -ErrorAction Stop).CodexBootAnimation
    }
    $state = 'NotConfigured'
    if ($null -ne $approval) {
        $state = 'Unknown'
        if ($approval -is [byte[]] -and $approval.Length -eq 12) {
            $code = [BitConverter]::ToUInt32($approval, 0)
            # Only known enabled/disabled records are conclusive. Unknown records
            # must not satisfy an explicit keep-disabled requirement.
            if ($code -eq 3) { $state = 'Disabled' }
            elseif ($code -eq 2) { $state = 'Enabled' }
        }
    }
    return [pscustomobject]@{ ManualOnly = $manualOnly; ApprovalState = $state }
}

function Assert-CompanionStartupPolicy {
    param($Status, [string]$RegisteredCommand, [switch]$RequireDisabled)
    if (($Status.ManualOnly -or $RequireDisabled) -and $RegisteredCommand -and $Status.ApprovalState -ne 'Disabled') {
        throw 'Sign-in startup is not verified disabled. Disable CodexBootAnimation in Windows Startup Apps before retrying; the saved manual-start preference was preserved.'
    }
}

function Resolve-IsolatedRuntimeRoot {
    param([string]$RuntimeRoot)
    # Compile the exact validator included in BootPlayer, only for test mode.
    # Normal startup installation has no dependency on the source tree.
    if (-not ('BootAnimationTestRoot' -as [type])) {
        Add-Type -Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'src\IsolatedRuntimeRoot.cs')
    }
    $realRoot = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex-boot-animation'
    return [BootAnimationTestRoot]::Validate($RuntimeRoot, $realRoot)
}

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

# One absolute deadline covers adopt, lifetime, pipe probe and readiness. Each
# step only receives the remaining budget; a late success is still a timeout.
function New-InstallBudget {
    param([int]$TimeoutSeconds = 60)
    return [pscustomobject]@{ Timer = [Diagnostics.Stopwatch]::StartNew(); Seconds = $TimeoutSeconds }
}
function Get-InstallRemainingMs {
    param($Budget)
    $remaining = [int]($Budget.Seconds * 1000 - $Budget.Timer.ElapsedMilliseconds)
    if ($remaining -lt 0) { return 0 }
    return $remaining
}
function Get-InstallRemainingSeconds {
    param($Budget)
    return [int][Math]::Ceiling((Get-InstallRemainingMs -Budget $Budget) / 1000.0)
}
# Bounded wait for a helper this script started. On timeout only that child is
# terminated; no process is ever killed by name.
function Invoke-BoundedCompanion {
    param([string]$Exe, [string[]]$Arguments, [int]$TimeoutMs)
    if ($TimeoutMs -le 0) { return [pscustomobject]@{ ExitCode = $null; TimedOut = $true } }
    $child = Start-Process -FilePath $Exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $child.WaitForExit($TimeoutMs)) {
            try { $child.Kill() } catch { }
            try { $null = $child.WaitForExit(1000) } catch { }
            return [pscustomobject]@{ ExitCode = $null; TimedOut = $true }
        }
        return [pscustomobject]@{ ExitCode = $child.ExitCode; TimedOut = $false }
    } finally { $child.Dispose() }
}
function Wait-CodexCompanion {
    param([string]$Exe, [int]$TimeoutSeconds = 20)
    $budget = New-InstallBudget -TimeoutSeconds $TimeoutSeconds
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
            $lifetime = Invoke-BoundedCompanion -Exe $Exe -Arguments @('--lifetime-check', $supervisor.ProcessId) -TimeoutMs (Get-InstallRemainingMs -Budget $budget)
            if ($lifetime.TimedOut -or $lifetime.ExitCode -ne 0) { $ready = $false }
        }
        if ($ready) {
            # An invalid idle token only checks IPC; it never requests playback.
            $probe = Invoke-BoundedCompanion -Exe $Exe -Arguments @('--idle-pipe-probe') -TimeoutMs (Get-InstallRemainingMs -Budget $budget)
            if (-not $probe.TimedOut -and $probe.ExitCode -eq 0) { return $true }
        }
        $remaining = Get-InstallRemainingMs -Budget $budget
        if ($remaining -le 0) { break }
        Start-Sleep -Milliseconds ([Math]::Min(300, $remaining))
    } while ($true)
    return $false
}
