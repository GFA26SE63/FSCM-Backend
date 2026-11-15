#Requires -Version 7.0
[CmdletBinding()]
param([int]$StartupTimeoutSeconds = 60)

$ErrorActionPreference = 'Stop'
$backendRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$factoryRoot = Join-Path $backendRoot '../FSCM-Traceability'
$runLogs = Join-Path ([IO.Path]::GetTempPath()) ('FSCM-FactoryStartup-' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
New-Item -ItemType Directory -Path $runLogs -Force | Out-Null
$factorySettings = Get-Content -LiteralPath (Join-Path $factoryRoot 'appsettings.Local.json') -Raw | ConvertFrom-Json
$configuredUrl = [Environment]::GetEnvironmentVariable('Fscm__BaseUrl')
if ([string]::IsNullOrWhiteSpace($configuredUrl)) { $configuredUrl = $factorySettings.Fscm.BaseUrl }
if ([string]::IsNullOrWhiteSpace($configuredUrl)) { throw 'Fscm:BaseUrl is missing in the factory configuration.' }
$fscmUri = [Uri]$configuredUrl
$factoryUri = [Uri]'http://localhost:5250/'

function Probe-Health([Uri]$BaseUri) {
    $taskHttp = [System.Net.Http.HttpClient]::new()
    $taskHttp.Timeout = [TimeSpan]::FromSeconds(3)
    try {
        $response = $taskHttp.GetAsync([Uri]::new($BaseUri, 'health')).GetAwaiter().GetResult()
        try { return @{ Ready = $response.IsSuccessStatusCode; Error = "HTTP $([int]$response.StatusCode)" } }
        finally { $response.Dispose() }
    } catch { return @{ Ready = $false; Error = $_.Exception.GetBaseException().Message } }
    finally { $taskHttp.Dispose() }
}
function Port-IsOpen([Uri]$Target) {
    $taskSocket = [System.Net.Sockets.TcpClient]::new()
    try { return $taskSocket.ConnectAsync($Target.DnsSafeHost, $Target.Port).Wait(1500) -and $taskSocket.Connected }
    catch { return $false }
    finally { $taskSocket.Dispose() }
}
function Ensure-App([string]$Name, [Uri]$BaseUri, [string]$ProjectRoot, [string]$Profile) {
    Write-Host "Checking $Name at $BaseUri"
    $health = Probe-Health $BaseUri
    if ($health.Ready) { Write-Host "$Name is already running and healthy."; return }
    if (!$BaseUri.IsLoopback) { throw "$Name is a remote server and cannot be started by this local script. Health error: $($health.Error)" }
    if (Port-IsOpen $BaseUri) {
        throw "$Name port is open but /health failed: $($health.Error). If the error is a development HTTPS certificate, run: dotnet dev-certs https --trust. No duplicate process was started."
    }
    $project = Join-Path $ProjectRoot $(if ($Name -eq 'FSCM') { 'FSCM.API.csproj' } else { 'FSCM.Traceability.csproj' })
    $assembly = Join-Path $ProjectRoot $(if ($Name -eq 'FSCM') { 'bin/Debug/net10.0/FSCM.API.dll' } else { 'bin/Debug/net10.0/FSCM.Traceability.dll' })
    if (!(Test-Path -LiteralPath $assembly)) { throw "Build output missing for $Name. Build the project first: $project" }
    $outLog = Join-Path $runLogs "$Name.out.log"
    $errorLog = Join-Path $runLogs "$Name.err.log"
    $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
    # Separate hidden process continues after this runner exits. No password/key is passed on its command line.
    $process = Start-Process -FilePath $dotnetPath -ArgumentList @('run', '--project', ('"' + $project + '"'), '--no-build', '--launch-profile', $Profile) `
        -WorkingDirectory $ProjectRoot -WindowStyle Hidden -RedirectStandardOutput $outLog -RedirectStandardError $errorLog -PassThru
    Write-Host "Started $Name (launcher PID $($process.Id)). Waiting for /health..."
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    do {
        $health = Probe-Health $BaseUri
        if ($health.Ready) { Write-Host "$Name READY at $BaseUri"; return }
        $process.Refresh()
        if ($process.HasExited) { throw "$Name exited (code $($process.ExitCode)). Logs: $outLog and $errorLog" }
        Start-Sleep -Seconds 1
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "$Name did not become healthy in time: $($health.Error). Logs: $outLog and $errorLog. The launched process was left available for inspection."
}

try {
    if ($StartupTimeoutSeconds -lt 5 -or $StartupTimeoutSeconds -gt 120) { throw 'StartupTimeoutSeconds must be between 5 and 120.' }
    if ($fscmUri.IsLoopback -and $fscmUri.Port -notin @(5246, 7027)) { throw "FSCM is configured at $fscmUri; the available launch profiles use 5246 (http) and 7027 (https). Check Fscm:BaseUrl first." }
    $backendProfile = if ($fscmUri.Scheme -eq 'https') { 'https' } else { 'http' }
    Ensure-App -Name 'FSCM' -BaseUri $fscmUri -ProjectRoot (Join-Path $backendRoot 'src/FSCM.API') -Profile $backendProfile
    Ensure-App -Name 'Traceability' -BaseUri $factoryUri -ProjectRoot $factoryRoot -Profile 'http'
    Write-Host "`nBOTH APPS READY. Open http://localhost:5250/ and sign in with your FSCM Admin account."
    Write-Host 'Existing healthy processes were reused. This script does not migrate or change database data.'
    Write-Host "Startup logs (new processes only): $runLogs"
} catch {
    Write-Host "STARTUP FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Logs (if started): $runLogs"
    exit 1
}
