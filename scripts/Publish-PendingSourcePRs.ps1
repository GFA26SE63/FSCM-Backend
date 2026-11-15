#Requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))

# Only publish the six reviewed branches. Never stage working files, merge or deploy.
$changes = @(
    @{ Folder='FSCM-Backend'; Repo='FSCM-Backend'; Branch='feature/warehouse-traceability-backend-20261115'; Draft=$false;
       Title='feat: integrate warehouse documents, pallet inspection and factory ACK';
       Body=@'
Adds warehouse documents, separate picking/issue operations, pallet manifest preview and per-carton Accepted/Missing/Damaged inspection, quarantine records, QR inventory and retryable factory receipt acknowledgements over HTTP. Preserves factory codes and prevents replay from adding stock twice.

Includes warehouse migrations, integration tests, local operational scripts, and CI checkout of a pinned standalone Traceability dependency for cross-system tests. Production API has no project reference to the factory application.

Validation: local API build passed with zero warnings/errors. Full PostgreSQL acceptance and deployed end-to-end verification remain required; do not assume database-dependent tests ran merely because CI is green. Frontend build and 217 tests passed in the companion frontend checkout.

Deployment: apply AppDbContext warehouse migrations; configure Traceability__BaseUrl, Traceability__PublicUrl and Traceability__ApiKey (matching factory Integration__ApiKey). The standalone factory owns its own schema and migrations. scripts/Deploy-WarehousePicking.ps1 is a LOCAL stack helper, not an Azure deployment command. Review docs/WarehouseExceptionImplementation.md for supported operations and remaining gaps. No production deployment is performed by this PR.
'@ },
    @{ Folder='FSCM-Frontend'; Repo='FSCM-Frontend'; Branch='fix/portal-arial-font-20261115'; Draft=$false;
       Title='fix: use Arial consistently across portal, QR and maps';
       Body='Uses Arial for portal controls, QR identifiers, charts and embedded Leaflet labels; removes the external Google Fonts request. Validation: production build and all 217 frontend tests passed. The existing pallet integration is already merged via PR #148.' },
    @{ Folder='FSCM-Mobile'; Repo='FSCM-Mobile'; Branch='chore/mobile-lockfile-sync-20261115'; Draft=$true;
       Title='chore: preserve local Flutter dependency lockfile changes';
       Body='Preserves the local pubspec.lock changes for review. Resolved versions of intl, matcher, meta, test_api and vector_math differ from the previous lockfile. No application behavior changes are claimed. Draft: reconcile the intended Flutter SDK and run package resolution, analysis and tests on the team SDK before merging.' },
    @{ Folder='FSCM-Backend-review2-fix'; Repo='FSCM-Backend'; Branch='wip/review2-backend-remainder-20261115'; Draft=$true;
       Title='wip: preserve remaining review 2 backend changes';
       Body='Preserves six previously uncommitted files concerning ETA calculation, current picking, warehouse configuration, demo seeding and associated tests. This branch starts from the old review 2 checkout. Draft: compare with current main and port only still-needed changes; do not merge wholesale over newer fixes. Validation details must be confirmed by CI; no production or database actions performed.' },
    @{ Folder='FSCM-Frontend-review2-fix'; Repo='FSCM-Frontend'; Branch='wip/review2-frontend-remainder-20261115'; Draft=$true;
       Title='wip: preserve remaining review 2 portal changes';
       Body='Preserves 17 previously uncommitted files covering navigation, removed allocation wrappers, run schedule configuration, notifications, dashboard and picking contracts. Local production build passed. This is an old review checkout: compare with current main and port only still-needed changes; do not merge wholesale over the newer warehouse integration.' },
    @{ Folder='FSCM-Mobile-review2-fix'; Repo='FSCM-Mobile'; Branch='wip/review2-mobile-remainder-20261115'; Draft=$true;
       Title='wip: preserve remaining review 2 mobile changes';
       Body='Preserves 18 previously uncommitted files covering routing, warehouse screens, notification and receivable contracts, debt UI and picking. Draft: this checkout predates newer merged fixes. Compare with current main and port only necessary changes; validate with the team Flutter SDK before merge. No deployed end-to-end pass is claimed.' }
)

function Check-Command([string]$step) {
    if ($LASTEXITCODE -ne 0) { throw "$step failed (exit $LASTEXITCODE)." }
}
& gh auth status
Check-Command 'GitHub authentication'
$results = @()
foreach ($change in $changes) {
    $repoPath = Join-Path $packageRoot $change.Folder
    $bodyPath = $null
    try {
        $remote = (& git -C $repoPath remote get-url origin).Trim()
        Check-Command 'Read origin'
        if ($remote.TrimEnd('/') -notin @("https://github.com/GFA26SE63/$($change.Repo).git", "https://github.com/GFA26SE63/$($change.Repo)", "git@github.com:GFA26SE63/$($change.Repo).git")) {
            throw "Unexpected origin for $($change.Folder)."
        }
        & git -C $repoPath show-ref --verify --quiet "refs/heads/$($change.Branch)"
        Check-Command 'Check prepared branch'
        & git -C $repoPath push --set-upstream origin "refs/heads/$($change.Branch):refs/heads/$($change.Branch)"
        Check-Command 'Push prepared branch'
        $existingJson = & gh pr list --repo "GFA26SE63/$($change.Repo)" --head $change.Branch --state all --json url,state
        Check-Command 'Find existing PR'
        $existing = @($existingJson | ConvertFrom-Json) | Where-Object { $_.state -in @('OPEN','MERGED') } | Select-Object -First 1
        if ($existing) {
            $results += [PSCustomObject]@{Repo=$change.Repo; Branch=$change.Branch; Result=$existing.state; URL=$existing.url}
            continue
        }
        $bodyPath = Join-Path ([IO.Path]::GetTempPath()) ("fscm-pr-" + [Guid]::NewGuid().ToString('N') + '.md')
        [IO.File]::WriteAllText($bodyPath, $change.Body, [Text.UTF8Encoding]::new($false))
        $prArgs = @('pr','create','--repo',"GFA26SE63/$($change.Repo)",'--base','main','--head',$change.Branch,'--title',$change.Title,'--body-file',$bodyPath)
        if ($change.Draft) { $prArgs += '--draft' }
        $prOutput = & gh @prArgs
        Check-Command 'Create PR'
        $results += [PSCustomObject]@{Repo=$change.Repo; Branch=$change.Branch; Result='CREATED'; URL=($prOutput | Out-String).Trim()}
    } catch {
        $results += [PSCustomObject]@{Repo=$change.Repo; Branch=$change.Branch; Result='FAILED'; URL=$_.Exception.Message}
        Write-Warning "$($change.Folder): $($_.Exception.Message)"
    } finally {
        if ($bodyPath -and (Test-Path -LiteralPath $bodyPath)) { Remove-Item -LiteralPath $bodyPath -Force }
    }
}
$results | Format-List Repo,Branch,Result,URL
if (@($results | Where-Object Result -eq 'FAILED').Count) {
    throw 'Some branches/PRs failed. Fix the reported connection/access issue and rerun this script; existing open/merged PRs will be reused.'
}
Write-Host 'All six branches published and PRs available. No merge or deployment performed.' -ForegroundColor Green
