#Requires -Version 5.1
# Test children have a deadline even when a regression deadlocks before exit.
function Format-TestArguments {
    param([string[]]$ArgumentList)
    return (@($ArgumentList) | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
}
function Start-TestProcess {
    param([string]$FilePath, [string[]]$ArgumentList, [string]$WindowStyle = 'Hidden',
          [switch]$Wait, [switch]$PassThru, [int]$TimeoutSeconds = 60)
    $child = Start-Process -FilePath $FilePath -ArgumentList (Format-TestArguments $ArgumentList) -WindowStyle Hidden -PassThru
    if ($Wait -and -not $child.WaitForExit($TimeoutSeconds * 1000)) {
        $child.Kill()
        $child.WaitForExit(5000) | Out-Null
        $child.Dispose()
        throw "Test process timed out: $ArgumentList"
    }
    return $child
}
