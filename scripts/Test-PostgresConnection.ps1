[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '../src/FSCM.API/appsettings.json'),
    [ValidateSet("DefaultConnection", "TraceabilityConnection")]
    [string]$ConnectionName = "DefaultConnection",
    [int]$TimeoutSeconds = 5
)

$ErrorActionPreference = 'Stop'
if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 60) { throw 'TimeoutSeconds must be between 1 and 60.' }
$connectionText = [Environment]::GetEnvironmentVariable('ConnectionStrings__' + $ConnectionName)
if ([string]::IsNullOrWhiteSpace($connectionText)) {
    $settings = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $connectionText = $settings.ConnectionStrings.$ConnectionName
}
if ([string]::IsNullOrWhiteSpace($connectionText)) { throw "$ConnectionName is missing." }
$parts = [System.Data.Common.DbConnectionStringBuilder]::new()
$parts.set_ConnectionString($connectionText)
function Read-Setting([string[]]$Names, [string]$Fallback = '') {
    foreach ($name in $Names) { if ($parts.ContainsKey($name)) { return [string]$parts.get_Item($name) } }
    return $Fallback
}
$databaseHost = Read-Setting @('Host', 'Server')
$databasePort = [int](Read-Setting @('Port') '5432')
$databaseName = Read-Setting @('Database') 'postgres'
$databaseUser = Read-Setting @('Username', 'User ID', 'UserId', 'User Name', 'UID')
$databasePassword = Read-Setting @('Password', 'PWD')
$sslMode = (Read-Setting @('SSL Mode', 'SSLMode') 'Prefer').ToLowerInvariant().Replace('verifyfull', 'verify-full').Replace('verifyca', 'verify-ca')
if (!$databaseHost -or !$databaseUser) { throw 'Host or Username is missing.' }
Write-Host "Target: ${databaseHost}:$databasePort / $databaseName / SSL=$sslMode"
Write-Host 'This check does not apply migrations or modify data. Passwords are not printed.'

try {
    $lookup = [System.Net.Dns]::GetHostAddressesAsync($databaseHost)
    if (!$lookup.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) { throw 'DNS lookup timed out.' }
    Write-Host "[1/3 DNS OK] $($lookup.Result.IPAddressToString -join ', ')"
} catch {
    Write-Host '[1/3 DNS FAILED] Cannot resolve the database hostname from this PowerShell session.'
    Write-Host $_.Exception.GetBaseException().Message
    exit 1
}

$client = [System.Net.Sockets.TcpClient]::new()
try {
    $connect = $client.ConnectAsync($databaseHost, $databasePort)
    if (!$connect.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) { throw 'TCP connection timed out.' }
    Write-Host '[2/3 TCP OK] Database port is reachable.'
} catch {
    Write-Host '[2/3 TCP FAILED] Connection failed before database authentication.'
    Write-Host $_.Exception.GetBaseException().Message
    exit 2
} finally { $client.Dispose() }

$psqlCommand = Get-Command psql.exe -ErrorAction SilentlyContinue
$psqlPath = if ($psqlCommand) { $psqlCommand.Source } else { Join-Path $env:ProgramFiles 'PostgreSQL/18/bin/psql.exe' }
if (!(Test-Path -LiteralPath $psqlPath)) { Write-Host '[3/3 SKIPPED] psql.exe was not found. DNS/TCP passed; authentication remains unverified.'; exit 3 }
$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = $psqlPath
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($argument in @('-X', '-w', '-h', $databaseHost, '-p', [string]$databasePort, '-U', $databaseUser, '-d', $databaseName,
    '-v', 'ON_ERROR_STOP=1', '-c', 'SELECT current_database(), current_user, current_timestamp, current_setting(''server_version'');')) {
    $start.ArgumentList.Add($argument)
}
$start.Environment['PGPASSWORD'] = $databasePassword
$start.Environment['PGSSLMODE'] = $sslMode
$start.Environment['PGCONNECT_TIMEOUT'] = [string]$TimeoutSeconds
$process = [System.Diagnostics.Process]::Start($start)
$outputTask = $process.StandardOutput.ReadToEndAsync()
$errorTask = $process.StandardError.ReadToEndAsync()
if (!$process.WaitForExit(($TimeoutSeconds + 5) * 1000)) {
    $process.Kill($true)
    Write-Host '[3/3 SQL FAILED] Connection/query timed out.'
    exit 4
}
$message = $outputTask.GetAwaiter().GetResult() + $errorTask.GetAwaiter().GetResult()
if ($databasePassword) { $message = $message.Replace($databasePassword, '[REDACTED]') }
if ($process.ExitCode -ne 0) { Write-Host '[3/3 SQL FAILED] DNS/TCP passed. PostgreSQL/TLS authentication details follow:'; Write-Host $message; exit 4 }
Write-Host '[3/3 SQL OK] Connected and executed a read-only query.'
Write-Host $message
