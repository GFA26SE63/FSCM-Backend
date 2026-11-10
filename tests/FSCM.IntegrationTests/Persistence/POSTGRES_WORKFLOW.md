# PostgreSQL workflow verification

Run against a **fresh, disposable local** PostgreSQL database named `fscm_e2e_*`.
The test applies real EF migrations and development seed data. It rejects remote hosts and other database names.

```powershell
$env:FSCM_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=fscm_e2e_run;Username=postgres'
dotnet test tests/FSCM.IntegrationTests --filter FullyQualifiedName~PostgresWorkflowTests
```

Without the environment variable, the PostgreSQL fact is explicitly skipped.

Coverage: real scoped application/persistence services, multiwarehouse allocation, quote acceptance/rejection,
expiration and reservation release, QR generation/picking, dispatch, database rollback when one warehouse's
receipt fails, successful receipt, 14-day receivable, cash collection, late PO after the first order has completed,
reopening picking sheets without duplicating splits, and no remaining reservations for the test SKU.
The routes and DTOs of Mobile and Portal are checked in their own contract suites. This is not browser automation.
PayOS and live payment transactions are excluded.
