#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '../src/FSCM.API/appsettings.json'),
    [string]$FactoryConfigPath = (Join-Path $PSScriptRoot '../../FSCM-Traceability/appsettings.Local.json')
)

$ErrorActionPreference = 'Stop'
$backendRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runId = (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$results = Join-Path ([IO.Path]::GetTempPath()) "FSCM-Integration-$runId"
$artifacts = Join-Path $results 'artifacts'
$testProject = Join-Path $backendRoot 'tests/FSCM.IntegrationTests/FSCM.IntegrationTests.csproj'
$saved = @{}
foreach ($key in @('FSCM_TEST_POSTGRES', 'FSCM_TEST_POSTGRES_ALLOWED_HOST', 'ConnectionStrings__DefaultConnection', 'ConnectionStrings__TraceabilityConnection')) {
    $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}
function Run-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE). Integration is NOT verified." }
}
try {
    $wms = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $factory = Get-Content -LiteralPath $FactoryConfigPath -Raw | ConvertFrom-Json
    $connection = if ($saved['ConnectionStrings__DefaultConnection']) { $saved['ConnectionStrings__DefaultConnection'] } else { $wms.ConnectionStrings.DefaultConnection }
    $factoryConnection = if ($saved['ConnectionStrings__TraceabilityConnection']) { $saved['ConnectionStrings__TraceabilityConnection'] } else { $factory.ConnectionStrings.TraceabilityConnection }
    if (!$connection -or !$factoryConnection) { throw 'Both WMS and factory connection strings are required.' }
    $target = [System.Data.Common.DbConnectionStringBuilder]::new()
    $target.set_ConnectionString($connection)
    $factoryTarget = [System.Data.Common.DbConnectionStringBuilder]::new()
    $factoryTarget.set_ConnectionString($factoryConnection)
    $databaseHost = if ($target.ContainsKey('Host')) { [string]$target.get_Item('Host') } else { [string]$target.get_Item('Server') }
    if (!$databaseHost -or !$target.ContainsKey('Database')) { throw 'WMS Host and Database must be explicit.' }
    if (!$factoryTarget.ContainsKey('Database')) { throw 'Factory Database must be explicit.' }
    $factoryHost = if ($factoryTarget.ContainsKey('Host')) { [string]$factoryTarget.get_Item('Host') } else { [string]$factoryTarget.get_Item('Server') }
    if ($databaseHost -eq $factoryHost -and $target.get_Item('Database') -eq $factoryTarget.get_Item('Database')) { throw 'WMS and Traceability must use separate databases.' }
    $wmsKey = if ($env:Traceability__ApiKey) { $env:Traceability__ApiKey } else { $wms.Traceability.ApiKey }
    $factoryKey = if ($env:Integration__ApiKey) { $env:Integration__ApiKey } else { $factory.Integration.ApiKey }
    if (!$wmsKey -or !$factoryKey -or $wmsKey -cne $factoryKey) { throw 'Integration API keys are absent or do not match. Keys are not printed.' }
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    [Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', $connection, 'Process')
    [Environment]::SetEnvironmentVariable('ConnectionStrings__TraceabilityConnection', $factoryConnection, 'Process')
    foreach ($name in @('DefaultConnection', 'TraceabilityConnection')) {
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-PostgresConnection.ps1') -ConfigPath $ConfigPath -ConnectionName $name
        if ($LASTEXITCODE -ne 0) { throw "PostgreSQL preflight failed: $name. No migrations applied." }
    }
    $testDatabase = 'fscm_e2e_integration_' + $runId.ToLowerInvariant()
    $target.set_Item('Database', $testDatabase)
    [Environment]::SetEnvironmentVariable('FSCM_TEST_POSTGRES', $target.get_ConnectionString(), 'Process')
    [Environment]::SetEnvironmentVariable('FSCM_TEST_POSTGRES_ALLOWED_HOST', $databaseHost, 'Process')
    Write-Host "Isolated workflow databases: $testDatabase and ${testDatabase}_trace"
    Write-Host 'Application data/schema are unchanged. Tests migrate and seed only these fresh databases; databases are retained.'
    Write-Host 'Factory HTTP runs through the real in-process API pipeline; this is not a deployed endpoint smoke test.'
    $buildOptions = @('-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$artifacts", '-p:NuGetAudit=false', '-m:1', '/nodeReuse:false')
    Run-Dotnet -Arguments (@('restore', $testProject, '--ignore-failed-sources', '--disable-parallel') + $buildOptions)
    Run-Dotnet -Arguments (@('build', $testProject, '--no-restore', '-v', 'minimal') + $buildOptions)
    $filter = 'FullyQualifiedName~FactoryTraceabilityTests|FullyQualifiedName~PhysicalSchemaModelTests|FullyQualifiedName~StandaloneTraceabilityTests|FullyQualifiedName~WarehouseDocumentsPostgresTests'
    Run-Dotnet -Arguments (@('test', $testProject, '--no-build', '--no-restore', '--filter', $filter,
        '--logger', 'trx;LogFileName=integration.trx', '--results-directory', $results) + $buildOptions)
    [xml]$report = Get-Content -LiteralPath (Join-Path $results 'integration.trx') -Raw
    $counts = $report.SelectSingleNode("//*[local-name()='Counters']")
    if (!$counts -or [int]$counts.executed -lt 40 -or [int]$counts.failed -ne 0 -or [int]$counts.notExecuted -ne 0) {
        throw 'Missing, skipped or failed integration checks. See integration.trx.'
    }
    Write-Host 'PASSED: factory HTTP -> pallet receipt -> QR inventory/ACK -> transfer -> quotation acceptance -> grouped order issue.' -ForegroundColor Green
    Write-Host "Reports: $results"
} catch {
    Write-Host "STOPPED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Reports (if created): $results"
    exit 1
} finally {
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
}
