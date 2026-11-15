#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$backendRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = [IO.Path]::GetFullPath((Join-Path $backendRoot '../FSCM-Frontend'))
$apiRoot = Join-Path $backendRoot 'src/FSCM.API'
$factoryRoot = Join-Path $backendRoot '../FSCM-Traceability'
function Check-Exit([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed ($LASTEXITCODE). Deployment stopped." }
}
try {
    # Validate the new workflow on isolated databases before changing the application database.
    & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Test-FactoryIntegration.ps1')
    Check-Exit 'PostgreSQL integration tests'
    Push-Location $frontendRoot
    try {
        & npm.cmd test
        Check-Exit 'Frontend tests'
        & npm.cmd run build
        Check-Exit 'Frontend build'
    } finally { Pop-Location }

    # Stop only this checkout's API, never an unrelated listener or all dotnet processes.
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 5246,7027,5250 -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    $owned = @()
    foreach ($apiProcessId in $listeners) {
        $info = Get-CimInstance Win32_Process -Filter "ProcessId=$apiProcessId"
        $exe = [string]$info.ExecutablePath
        $command = [string]$info.CommandLine
        $recognized = $false
        foreach ($app in @(@{ Root = $apiRoot; Name = 'FSCM.API' }, @{ Root = $factoryRoot; Name = 'FSCM.Traceability' }, @{ Root = (Join-Path $backendRoot 'standalone/FSCM.Traceability'); Name = 'FSCM.Traceability' })) {
            $appHost = $exe.StartsWith($app.Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
                [IO.Path]::GetFileName($exe) -eq ($app.Name + '.exe')
            $dotnetHost = [IO.Path]::GetFileName($exe) -eq 'dotnet.exe' -and
                $command.Contains($app.Root, [StringComparison]::OrdinalIgnoreCase) -and
                $command.Contains(($app.Name + '.dll'), [StringComparison]::OrdinalIgnoreCase)
            if ($appHost -or $dotnetHost) { $recognized = $true }
        }
        if (!$recognized) { throw "Unrecognized listener PID $apiProcessId. No processes stopped." }
        $owned += $apiProcessId
    }
    foreach ($apiProcessId in $owned) {
        Stop-Process -Id $apiProcessId -ErrorAction Stop
        Wait-Process -Id $apiProcessId -Timeout 15 -ErrorAction SilentlyContinue
    }
    Push-Location $backendRoot
    try {
        & dotnet build src/FSCM.API/FSCM.API.csproj --no-restore -m:1 /nodeReuse:false -v minimal
        Check-Exit 'Backend build'
        & dotnet build ../FSCM-Traceability/FSCM.Traceability.csproj -m:1 /nodeReuse:false -v minimal
        Check-Exit 'Traceability build'
        & dotnet run --project ../FSCM-Traceability --no-build --launch-profile http -- --migrate
        Check-Exit 'Traceability inspection migration'
        & dotnet ef database update --context AppDbContext --project src/FSCM.Persistence --startup-project src/FSCM.API --no-build
        Check-Exit 'Application database migration'
    } finally { Pop-Location }
    & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Start-FactoryStack.ps1')
    Check-Exit 'Application startup'
    Write-Host 'LOCAL STACK UPDATED: picking and receipt inspection migrations applied; both API health checks passed.' -ForegroundColor Green
    Write-Host 'Frontend dist rebuilt. Local Vite dev uses updated source; externally hosted frontend requires its own publish step.'
    Write-Host 'Reload the warehouse documents page with Ctrl+F5. Receipt inspection schema was updated in both databases; existing stock was not re-seeded.'
} catch {
    Write-Host "DEPLOYMENT INCOMPLETE: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'If API was already stopped, it may remain stopped. Completed migrations are not automatically undone.'
    exit 1
}
