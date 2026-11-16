#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$backendRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$factoryRoot = [IO.Path]::GetFullPath((Join-Path $backendRoot '../FSCM-Traceability'))
try {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 5250 -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
    foreach ($factoryProcessId in $listeners) {
        $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId=$factoryProcessId"
        $executable = [string]$processInfo.ExecutablePath
        $commandLine = [string]$processInfo.CommandLine
        $ownAppHost = $executable -and $executable.StartsWith($factoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($executable) -eq 'FSCM.Traceability.exe'
        $ownDotnet = [IO.Path]::GetFileName($executable) -eq 'dotnet.exe' -and $commandLine.Contains($factoryRoot, [StringComparison]::OrdinalIgnoreCase) -and
            $commandLine.Contains('FSCM.Traceability.dll', [StringComparison]::OrdinalIgnoreCase)
        if (!$ownAppHost -and !$ownDotnet) { throw "Port 5250 belongs to an unrecognized process (PID $factoryProcessId). It was not stopped." }
        Write-Host "Stopping this traceability instance (PID $factoryProcessId) to load the updated detail API."
        Stop-Process -Id $factoryProcessId -ErrorAction Stop
        Wait-Process -Id $factoryProcessId -Timeout 15 -ErrorAction SilentlyContinue
    }
    Push-Location $backendRoot
    try {
        & dotnet build (Join-Path $factoryRoot 'FSCM.Traceability.csproj') -m:1 /nodeReuse:false -v minimal
        if ($LASTEXITCODE -ne 0) { throw 'Traceability build failed; no new instance was started.' }
    } finally { Pop-Location }
    & (Join-Path $PSScriptRoot 'Start-FactoryStack.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Startup failed; inspect the startup log printed above.' }
    Write-Host 'Updated batch detail/mapping is ready. Reload http://localhost:5250/ with Ctrl+F5.'
} catch {
    Write-Host "RESTART FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
