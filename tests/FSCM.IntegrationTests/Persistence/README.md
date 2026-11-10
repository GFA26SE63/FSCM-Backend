# Real-time PostgreSQL workflow

`RealTimePostgresWorkflowTests` exercises the current service implementations and EF stores against real PostgreSQL, including migrations and development seeding. It covers immediate allocation, insufficient stock without partial holds, credit quote acceptance, PO/current picking, two-warehouse issue/assignment/dispatch/receipt, delivery-based debt dates and cash collection replay. Rejected and expired quotes must release their holds; all test holds must be zero after fulfillment.

Run from the repository root with a local PostgreSQL server:

```powershell
$env:FSCM_TEST_POSTGRES = 'Host=127.0.0.1;Port=5432;Database=fscm_e2e_realtime_unique_name;Username=postgres;Password=your_local_password'
dotnet test tests/FSCM.IntegrationTests --filter FullyQualifiedName~RealTimePostgresWorkflowTests
```

Supply a **new database name on every run**. The test accepts only loopback hosts and names starting with `fscm_e2e_`, refuses an existing database, and leaves its database for inspection. Without the environment variable, the test is skipped. The fixture places two warehouses inside the delivery radius and prepares fresh seven-day batches with 48 cartons each.

This follows `develop`'s real-time allocation and per-PO picking model. It does not call scheduled allocation, operator plan approval or legacy wave picking. It is a service/persistence integration test, not a browser or device end-to-end test. PayOS checkout/webhooks, prepaid fulfillment, offline submission, concurrent allocation races and discrepancy/claim flows are outside this test's coverage.
