#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$ConfigPath = (Join-Path $PSScriptRoot '../src/FSCM.API/appsettings.json'),
    [switch]$SkipMigration
)

$ErrorActionPreference = 'Stop'
$backendRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frontendRoot = Join-Path $backendRoot '../FSCM-Frontend'
$testProject = 'tests/FSCM.IntegrationTests/FSCM.IntegrationTests.csproj'
$runId = (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$testDatabase = 'fscm_e2e_warehouse_' + $runId.ToLowerInvariant()
$results = Join-Path ([IO.Path]::GetTempPath()) "FSCM-WarehouseValidation-$runId"
$savedEnvironment = @{}
foreach ($key in @('ConnectionStrings__DefaultConnection', 'ConnectionStrings__TraceabilityConnection', 'FSCM_TEST_POSTGRES', 'FSCM_TEST_POSTGRES_ALLOWED_HOST', 'ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT')) {
    $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
}

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE). Stopped; do not assume migration/tests succeeded." }
}
function Check-Trx([string]$Path, [int]$MinimumExecuted) {
    if (!(Test-Path -LiteralPath $Path)) { throw "Test report missing: $Path" }
    [xml]$report = Get-Content -LiteralPath $Path -Raw
    $counters = $report.SelectSingleNode("//*[local-name()='Counters']")
    if (!$counters -or [int]$counters.executed -lt $MinimumExecuted -or [int]$counters.failed -ne 0 -or [int]$counters.notExecuted -ne 0) {
        throw 'Tests did not all execute successfully (missing/skipped/failed tests). See the TRX report.'
    }
}

try {
    Get-Command dotnet -ErrorAction Stop | Out-Null
    Get-Command npm.cmd -ErrorAction Stop | Out-Null
    if (!(Test-Path -LiteralPath $frontendRoot)) { throw "Frontend checkout missing: $frontendRoot" }
    $settings = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $connectionText = $savedEnvironment['ConnectionStrings__DefaultConnection']
    if ([string]::IsNullOrWhiteSpace($connectionText)) {
        $settings = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
        $connectionText = $settings.ConnectionStrings.DefaultConnection
    }
    if ([string]::IsNullOrWhiteSpace($connectionText)) { throw 'DefaultConnection is missing.' }
    $traceConnectionText = $savedEnvironment['ConnectionStrings__TraceabilityConnection']
    if ([string]::IsNullOrWhiteSpace($traceConnectionText)) { $traceConnectionText = $settings.ConnectionStrings.TraceabilityConnection }
    if ([string]::IsNullOrWhiteSpace($traceConnectionText)) { throw 'TraceabilityConnection is missing; configure the separate factory database first.' }
    $factoryBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $factoryBuilder.set_ConnectionString($traceConnectionText)
    $factoryDatabase = [string]$factoryBuilder.get_Item('Database')
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder.set_ConnectionString($connectionText)
    $databaseHost = if ($builder.ContainsKey('Host')) { [string]$builder.get_Item('Host') } else { [string]$builder.get_Item('Server') }
    $applicationDatabase = [string]$builder.get_Item('Database')
    if ($factoryDatabase -eq $applicationDatabase -or !$factoryDatabase) { throw 'Factory database must be separate from the application database.' }
    if (!$databaseHost -or !$applicationDatabase) { throw 'The application connection must explicitly specify Host and Database.' }
    if (!$testDatabase.StartsWith('fscm_e2e_') -or $testDatabase -eq $applicationDatabase) { throw 'Invalid isolated test database.' }
    $builder.set_Item('Database', $testDatabase)
    $testConnection = $builder.get_ConnectionString()
    New-Item -ItemType Directory -Path $results -Force | Out-Null
    Write-Host "Application target: $databaseHost / $applicationDatabase"
    Write-Host "Factory database: $factoryDatabase"
    Write-Host "Isolated workflow test database: $testDatabase (created on the same server; and ${testDatabase}_trace; retained for inspection)"
    Write-Host 'Application changes are schema migration and copying existing factory identities to the separate factory database. Demo records go only into two isolated test databases.'

    Push-Location $backendRoot
    try {
        [Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', $connectionText, 'Process')
        [Environment]::SetEnvironmentVariable('ConnectionStrings__TraceabilityConnection', $traceConnectionText, 'Process')
        [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Production', 'Process')
        [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Production', 'Process')
        Write-Host "`n[1/6] Verify application PostgreSQL connectivity"
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-PostgresConnection.ps1') -ConfigPath $ConfigPath
        if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL preflight failed. No migration applied by this runner.' }
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-PostgresConnection.ps1') -ConfigPath $ConfigPath -ConnectionName TraceabilityConnection
        if ($LASTEXITCODE -ne 0) { throw 'Traceability PostgreSQL preflight failed. No migration applied by this runner.' }

        Write-Host "`n[2/6] Restore/build backend and verify migration tooling"
        Invoke-Dotnet -Arguments @('ef', '--version')
        Invoke-Dotnet -Arguments @('restore', $testProject, '--disable-parallel', '/m:1', '/nodeReuse:false')
        Invoke-Dotnet -Arguments @('build', $testProject, '--no-restore', '-m:1', '/nodeReuse:false', '-v', 'minimal')
        Invoke-Dotnet -Arguments @('restore', 'tools/FSCM.TraceabilityMaintenance/FSCM.TraceabilityMaintenance.csproj', '--disable-parallel', '/m:1', '/nodeReuse:false')
        Invoke-Dotnet -Arguments @('build', 'tools/FSCM.TraceabilityMaintenance/FSCM.TraceabilityMaintenance.csproj', '--no-restore', '-m:1', '/nodeReuse:false')
        foreach ($context in @('AppDbContext')) {
        Invoke-Dotnet -Arguments @('ef', 'migrations', 'has-pending-model-changes', '--context', $context, '--project', 'src/FSCM.Persistence', '--startup-project', 'src/FSCM.API', '--no-build')
        }

        Invoke-Dotnet -Arguments @('ef', 'migrations', 'has-pending-model-changes', '--project', '../FSCM-Traceability', '--startup-project', '../FSCM-Traceability', '--context', 'FactoryDbContext', '--no-build')
        Write-Host "`n[3/6] Run factory identity/validation/schema tests"
        Invoke-Dotnet -Arguments @('test', $testProject, '--no-build', '--no-restore', '--filter',
            'FullyQualifiedName~FactoryTraceabilityTests|FullyQualifiedName~PhysicalSchemaModelTests|FullyQualifiedName~StandaloneTraceabilityTests',
            '--logger', 'trx;LogFileName=factory-schema.trx', '--results-directory', $results)
        Check-Trx -Path (Join-Path $results 'factory-schema.trx') -MinimumExecuted 30
        & node --test ../FSCM-Traceability/tests/batch-details.test.cjs
        if ($LASTEXITCODE -ne 0) { throw 'Traceability detail/mapping UI regression tests failed.' }

        if ($SkipMigration) {
            Write-Host "`n[4/6] SKIPPED both database migrations (-SkipMigration); using existing schemas."
        } else {
            Write-Host "`n[4/6] Apply migrations to WMS and separate factory databases"
            Invoke-Dotnet -Arguments @('ef', 'database', 'update', '--context', 'AppDbContext', '--project', 'src/FSCM.Persistence', '--startup-project', 'src/FSCM.API', '--no-build')
            Invoke-Dotnet -Arguments @('run', '--project', '../FSCM-Traceability', '--no-build', '--no-restore', '--', '--migrate')
            Write-Host 'MIGRATION OK. Application schema is updated; no factory demo stock was inserted here.'
        }

        Invoke-Dotnet -Arguments @('run', '--project', 'tools/FSCM.TraceabilityMaintenance', '--no-build', '--no-restore', '--', 'copy-legacy', $ConfigPath)
        Invoke-Dotnet -Arguments @('run', '--project', 'tools/FSCM.TraceabilityMaintenance', '--no-build', '--no-restore', '--', 'verify', $ConfigPath)
        Write-Host "`n[5/6] Run real PostgreSQL warehouse/factory workflow on fresh isolated test database"
        [Environment]::SetEnvironmentVariable('FSCM_TEST_POSTGRES', $testConnection, 'Process')
        [Environment]::SetEnvironmentVariable('FSCM_TEST_POSTGRES_ALLOWED_HOST', $databaseHost, 'Process')
        # This suite explicitly creates a fresh database through postgres, then migrates/seeds it.
        # Never point this variable at the application database.
        Invoke-Dotnet -Arguments @('test', $testProject, '--no-build', '--no-restore', '--filter',
            'FullyQualifiedName~WarehouseDocumentsPostgresTests', '--logger', 'trx;LogFileName=warehouse-workflow.trx', '--results-directory', $results)
        Check-Trx -Path (Join-Path $results 'warehouse-workflow.trx') -MinimumExecuted 1
    } finally { Pop-Location }

    Write-Host "`n[6/6] Build and test frontend"
    Push-Location $frontendRoot
    try {
        if (!(Test-Path -LiteralPath 'node_modules/typescript')) {
            & npm.cmd ci
            if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
        }
        & npm.cmd run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
        & npm.cmd test
        if ($LASTEXITCODE -ne 0) { throw 'Frontend tests failed.' }
    } finally { Pop-Location }
    Write-Host "`nALL CHECKS PASSED: factory/warehouse workflow and frontend tests passed."
    if ($SkipMigration) { Write-Host 'Both database migrations were skipped as requested.' }
    else { Write-Host 'Both database migrations completed.' }
    Write-Host "Test databases retained: $testDatabase and ${testDatabase}_trace"
    Write-Host "Reports: $results"
} catch {
    Write-Host "`nSTOPPED: $($_.Exception.Message)" -ForegroundColor Red
    foreach ($reportName in @('factory-schema.trx', 'warehouse-workflow.trx')) {
        $reportPath = Join-Path $results $reportName
        if (Test-Path -LiteralPath $reportPath) {
            try {
                [xml]$failureReport = Get-Content -LiteralPath $reportPath -Raw
                foreach ($errorInfo in $failureReport.SelectNodes("//*[local-name()='ErrorInfo']")) {
                    Write-Host "ROOT TEST ERROR ($reportName): $($errorInfo.Message)" -ForegroundColor Yellow
                }
            } catch { Write-Host "Could not read $reportName; inspect the report directory." }
        }
    }
    Write-Host "Reports (if created): $results"
    Write-Host "Test database (if created): $testDatabase"
    Write-Host 'If MIGRATION OK appeared, the application schema was already updated; a later test failure does not undo it.'
    exit 1
} finally {
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
}
