# FSCM Backend

[![Backend CI](https://github.com/GFA26SE63/FSCM-Backend/actions/workflows/backend-ci.yml/badge.svg)](https://github.com/GFA26SE63/FSCM-Backend/actions/workflows/backend-ci.yml)

.NET 10 API for the FSCM short shelf-life FMCG warehouse operations and distribution allocation system.

The solution follows Clean Architecture boundaries across API, Application, Domain, Infrastructure, Persistence, and Shared projects. It also contains unit, integration, and load-test projects.

## Related repositories

- [FSCM Frontend](https://github.com/GFA26SE63/FSCM-Frontend)
- [FSCM Mobile](https://github.com/GFA26SE63/FSCM-Mobile)

Cross-system architecture documentation is stored beside the local repositories under `FSCM/.docs` and `FSCM/.diagram`.

## Architecture and folder structure

The solution uses Clean Architecture plus feature-oriented application modules. Dependencies point inward:

```text
FSCM.API ───────► FSCM.Application ───────► FSCM.Domain
    │                    │                       ▲
    ├──► FSCM.Infrastructure ────────────────────┘
    └──► FSCM.Persistence ──► Application + Domain

FSCM.Application ───────► FSCM.Shared
```

`Domain` must not depend on ASP.NET Core, EF Core, PostgreSQL, HTTP clients, or another outer project. `Application` owns use cases and ports (interfaces); `Persistence` and `Infrastructure` implement those ports; `API` is the composition and transport boundary.

```text
src/
├─ FSCM.API/
│  ├─ Controllers/       REST transport only: routing, authorization, request mapping
│  ├─ Contracts/         HTTP request DTOs grouped by feature
│  ├─ GraphQL/           authenticated entity-query surface
│  ├─ Errors/            feature exception → RFC ProblemDetails mapping
│  ├─ Extensions/        DI, middleware pipeline and configuration composition
│  ├─ Options/           strongly typed external configuration
│  └─ Program.cs         composition root; no business rules
├─ FSCM.Application/
│  ├─ Features/<Feature>/
│  │  ├─ <Feature>Service.cs               use-case orchestration and business policy
│  │  ├─ I<Feature>Store.cs                 persistence port owned by Application
│  │  ├─ <Feature>Contracts.cs              commands/results shared with transports
│  │  └─ <Feature>OperationException.cs     stable business error codes
│  ├─ Abstractions/      cross-cutting ports (clock, identity, transactions, gateways)
│  └─ Common/            reusable application-level concerns, not feature dumping ground
├─ FSCM.Domain/
│  ├─ Entities/          persisted domain state and domain constants
│  ├─ Authorization/     roles, scope claim names and token purposes
│  ├─ Allocation/        deterministic allocation domain algorithm
│  └─ Common/            domain primitives shared by multiple aggregates
├─ FSCM.Persistence/
│  ├─ DbContexts/        EF Core context and model configuration entry point
│  ├─ <Feature>/         store implementation and database query/mapping logic
│  ├─ Interceptors/      EF Core cross-cutting behavior
│  ├─ Migrations/        immutable migration history
│  └─ Seeding/           development-only deterministic dataset
├─ FSCM.Infrastructure/
│  ├─ Identity/          JWT and password hashing implementations
│  ├─ Payments/          payOS gateway adapter
│  ├─ Audit/, Rbac/      infrastructure cross-cutting adapters
│  └─ ExternalServices/  template/location for new third-party adapters
└─ FSCM.Shared/           small dependency-free primitives genuinely shared across layers

tests/
├─ FSCM.UnitTests/        tests feature services and domain algorithms without real I/O
├─ FSCM.IntegrationTests/ tests EF schema, GraphQL and external adapter boundaries
└─ FSCM.LoadTests/        repeatable performance/load scenarios
```

### Request flow

```text
REST Controller / GraphQL entity resolver
  → Application Feature Service
  → Application store/gateway interface
  → Persistence store or Infrastructure adapter
  → PostgreSQL / external service
```

Controllers and GraphQL resolvers must stay thin: bind input, resolve actor identity/role, call one application service, and map the transport result. Business validation, state transitions, idempotency, concurrency, allocation, pricing, and scope enforcement belong in Application/Domain. Persistence may optimize queries but must not become the owner of business policy.

### Adding or extending a feature

1. Put domain state/constants in `FSCM.Domain` only when they are independent of transport and storage.
2. Add contracts, operation exception, store/gateway port, and service under one `Application/Features/<Feature>` folder.
3. Implement database access under `Persistence/<Feature>` or third-party access under `Infrastructure/<Feature>`; register it in that project's `DependencyInjection.cs`.
4. Add REST request DTOs and a controller under `API`. Entity reads needed by Portal/Mobile may be exposed below the single `entities` GraphQL root through `IEntityQueryService`; GraphQL remains query-only.
5. Map feature exceptions to Problem Details in `API/Errors` and register the handler before the generic handler.
6. Add unit tests beside the matching feature test folder; add integration tests for EF mappings, authorization, GraphQL schema, or real adapter contracts.
7. For state-changing operations, preserve existing conventions: actor and scope checks, `expectedVersion`, `Idempotency-Key`, immutable history/audit rows, and cancellation tokens.

Do not reference `FSCM.API`, `FSCM.Persistence`, or `FSCM.Infrastructure` from Domain/Application. Do not expose EF entities directly from controllers. Do not place a feature's query logic in another feature store merely for convenience. Reuse `Features/_template` and the READMEs under `Application/Common`, `Infrastructure`, and `Domain/Common` when their concern applies.

## Prerequisites

- .NET SDK 10
- PostgreSQL for persistence-backed development

## Local development

Create an ignored local settings file from the committed template:

```powershell
Copy-Item src/FSCM.API/appsettings.example.json src/FSCM.API/appsettings.Local.json
dotnet restore FSCM.slnx
dotnet ef database update --project src/FSCM.Persistence/FSCM.Persistence.csproj --startup-project src/FSCM.API/FSCM.API.csproj --context AppDbContext
dotnet run --project src/FSCM.API/FSCM.API.csproj --launch-profile https
```

The development profile exposes Swagger at `/swagger`, GraphQL at `/graphql`, and health checks at `/health`.

Portal and Mobile use the authenticated `entities` GraphQL field as the only GraphQL query surface. Every exposed entity collection supports cursor pagination (`first`/`after` or `last`/`before`), `totalCount`, projection, filtering, and sorting; clients choose the page size and selected fields. Resolvers call the Application `IEntityQueryService`, whose Persistence implementation produces no-tracking `IQueryable` expressions. The API therefore never accesses `AppDbContext` directly. Role and warehouse/Sales-group/Retailer/order scope is applied by the service before client filters. Authentication credentials, token hashes, idempotency/outbox internals, retired StockHold/internal-transfer entities, and hash/operation-key fields are not exposed. Legacy feature-specific GraphQL roots, type extensions, and DataLoaders have been removed; the corresponding REST APIs are unchanged.

```graphql
query ProductsForMobile($after: String) {
  entities {
    products(
      first: 50
      after: $after
      where: { status: { eq: "Published" }, name: { contains: "sữa" } }
      order: [{ name: ASC }, { id: ASC }]
    ) {
      totalCount
      nodes { id sku name unit shelfLifeHours }
      pageInfo { hasNextPage endCursor }
    }
  }
}
```

### Identity, catalog, warehouse configuration, and development data

BE-01 uses Entra ID to authenticate Portal sign-in. The Portal sends its Entra bearer token to `POST /api/v1/auth/entra/exchange`; the API validates tenant, audience, signature and lifetime, matches a pre-provisioned active FSCM account by immutable `oid` or links a null `EntraObjectId` by normalized email on first exchange, and returns the same FSCM JWT/refresh-token contract used by the rest of the system. Authentication methods are additive: an account that also has a password credential can continue using `POST /api/v1/auth/login` after Entra linking. Protected REST and GraphQL operations accept only the internal FSCM JWT. Google sign-in is not enabled.

Configure `Entra:TenantId`, `Entra:ClientId`, `Jwt:Issuer`, `Jwt:Audience`, and a JWT secret of at least 32 UTF-8 bytes through environment variables, user secrets, or the deployment secret store. No credential is committed in `appsettings.json`. `/api/v1/auth/me` returns the current role plus warehouse, region, Sales-group, and Retailer scopes. Every request revalidates that the FSCM account is active and that the token role still matches the database, so status/role changes take effect without waiting for token expiry.

Admin user management is available under `/api/v1/users`: filtered deterministic paging, detail/profile updates, role/status commands, and warehouse/region/Sales-group/Retailer scope assignment. Entity reads are available through `entities`; the former `users`, `user`, `userScope`, and `me` GraphQL roots are removed. User creation accepts `identityProvider` as `Local` or `Entra` to select the initial provisioning flow: Entra-first accounts bind their immutable `oid` on first exchange, while Local-first accounts use activation and may later bind Entra without losing password login. Admin creation and promotion remain blocked in the public API.

Create the first deployment Admin exactly once from the deployment secret store:

```powershell
dotnet run --project src/FSCM.API/FSCM.API.csproj -- --bootstrap-admin
```

Supply `AdminBootstrap:Email`, `AdminBootstrap:FullName`, `AdminBootstrap:IdentityProvider`, and either `AdminBootstrap:EntraObjectId` or `AdminBootstrap:Password`. The command applies pending migrations, exits without changing an existing Admin, and never logs the password.

Create the development-only identity dataset after applying migrations:

```powershell
dotnet user-secrets set "SeedData:DefaultPassword" "LogisticsThesis@123" --project src/FSCM.API/FSCM.API.csproj
dotnet run --project src/FSCM.API/FSCM.API.csproj --launch-profile http -- --seed
```

The command is idempotent and creates active users for all six roles plus BE-02 catalog dump data. All seeded demo accounts use the canonical password shown above. It is for local development only and must not be used in a deployed environment.

| Role | Email | Seeded scope |
| --- | --- | --- |
| Admin | `admin@fscm.local` | System-wide administration |
| Operator | `operator@fscm.local` | All three demo warehouses |
| SalesManager | `sales.manager@fscm.local` | `HCM-A`, `RTL-HCM-001` |
| Sales | `sales@fscm.local` | `HCM-A`, `RTL-HCM-001` |
| WarehouseKeeper | `warehouse.keeper@fscm.local` | All three demo warehouses |
| Retailer | `retailer@fscm.local` | Retailer account only |

Admin accounts are bootstrap-only: the user-management API rejects both Admin creation and Admin role promotion.

BE-02 exposes authenticated category and product reads under `/api/v1/categories` and `/api/v1/products`. Admin-only commands manage category relationships, Draft products, publication, inactivation, replacement SKUs, and effective price history. Publishing a replacement SKU automatically inactivates its predecessor. Published product master data is immutable; price changes are recorded as history instead of overwriting prior values.

BE-03 exposes warehouse master data, coordinates, and the non-negative per-warehouse minimum-stock monitoring threshold under `/api/v1/warehouses`. Warehouse records are inactivated instead of deleted. The obsolete per-warehouse `/api/v1/warehouses/{warehouseId}/transit-time-rules` API and GraphQL fields are no longer public; delivery timing and estimated cost use the shared, versioned `/api/v1/transit-tables` contract.

Google Maps Platform is the server provider for address normalization and road distance. Configure `GoogleMaps:ServerApiKey` only through environment variables, user secrets, or a deployment secret store; use a separately restricted browser key for map rendering. Admin commands `POST /api/v1/map/geocode` and `POST /api/v1/map/distances/refresh` persist provider/location versions and a 24-hour distance cache. Map location and distance-cache entities are available through the scoped `entities` GraphQL root. `POST /api/v1/eta/estimate` uses only a current cached route plus the effective transit-rule version and calculates wait-for-run, approval, handling, preparation, next-dispatch wait, buffer, and transport components. Its result includes the distance/rule versions, estimated transport cost, and an explicit estimate disclaimer; provider failure or an unresolved route is never converted into zero kilometres.

BE-04 exposes region and Sales-group master data under `/api/v1/regions` and `/api/v1/sales-groups`. Admin and scoped Sales Managers can manage Sales and Retailer assignments under `/api/v1/territory-assignments`; every transfer closes the previous row and creates a new row so assignment history remains intact. `GET /api/v1/retailers?scope=mine&search=...&page=1&pageSize=20` returns only the active Sales user's territory, and list/detail responses contain the two logical declaration sections `generalInformation` and `mlorDeclarations`. MLOR entries select exactly one Category or SKU scope, require `0 < ratio < 1`, and retain effective version history. A draft is submitted with `POST /api/v1/retailers/{id}/submit-review`; submission, approval, and reactivation are blocked until at least one valid MLOR entry exists. `GET /api/v1/retailers/{id}/mlor-resolution?productId=...` resolves SKU, then the most-specific Category ancestor, then the versioned default. Only Admin may use the standalone `/api/v1/mlor-configurations` correction/history API. Admin approval activates the eight-digit Retailer code and fixes the final `0/7/14/30` payment term.

BE-09 exposes campaign management under `/api/v1/promotions`, shelf-life suggestions and batch assignments under `/api/v1/batch-discounts`, and the pre-allocation quote under `/api/v1/pricing/snapshot`. The public quote uses base price plus an eligible ordinary campaign only and returns `finalPricePendingAllocation=true`; it never accepts a client-selected batch or applies a batch/HSD discount. The response identifies the exact product-price record and effective window and includes the applied campaign's quantity/effective conditions. Batch-derived final pricing remains an internal post-allocation concern. GraphQL exposes entity reads only; calculated use-case results remain REST contracts.

Assisted ordering is exposed under `/api/v1/orders`. Sales can create an order only for an active retailer in their current territory; the server snapshots the exact base-price record/effective window, ordinary-campaign conditions, and the resolved Retailer MLOR source/scope/version for every line, then marks pricing pending the final batch-derived HSD calculation. Capture does not query or select inventory, create an allocation, hold/reserve stock, or collect payment. `GET /api/v1/orders` supports scoped `createdByMe`, status, retailer, search, and deterministic paging; `GET /api/v1/orders/{id}` uses the resource GUID and composes the current run result, immutable per-batch F98 price fragments, and picking/shipment progress per warehouse. Each fragment retains list price, ordinary-promotion reduction, base price, remaining-HSD percentage, rule scope/code/version/reason, HSD reduction, final unit price, and net amount without collapsing different prices of the same SKU. GraphQL `entities.orderReads` and `entities.orderRead` use the same scoped application service and DTO. The legacy approve/reject contract remains pending the separately tracked F36 rewrite.

Run picking is exposed under `/api/v1/picking-lists`. An approved allocation run issues one immutable-code list per warehouse. Each list gathers by SKU/batch across orders, then splits the picked quantity back to the approved `AllocationRunAllocationId`; every split retains its `AllocationPlanPriceId` and is never repriced. An assigned Warehouse Keeper scans only available QR labels for the exact warehouse and approved batch. Partial scans create child labels while preserving lineage; every scan is idempotent and version checked. `POST /api/v1/picking-lists/{code}/complete` marks the warehouse portion `Ready` only when every gather and split is full, or records `NeedsHandling` with a shortage/damage/expiry/QR exception. Scanning and readiness never change `OnHand` or `Reserved`. Scoped run-picking list/gather/split entities are queryable below GraphQL `entities`.

Goods receipt is exposed under `/api/v1/goods-receipts`. Admin and Warehouse Keeper create an idempotent `Pending` receipt with production and expiry dates; creation never changes inventory. `POST /api/v1/goods-receipts/{receiptId}/approve` is the only transition to `Received`: it idempotently creates or merges batches by warehouse, SKU, and expiry date, increases stock once, and records approval and inventory-source audit data. Inventory exposes only `physicalQuantity`, `reservedQuantity`, and the enforced invariant `availableQuantity = physicalQuantity - reservedQuantity`.

QR lookup and printable HTML labels are exposed under `/api/v1/qr-labels`. Single-label and batch print artifacts include QR payload, SKU, batch, warehouse, quantity, and expiry information. Splits require whole-carton quantities and preserve parent/root lineage and quantity conservation. The generic scan endpoint accepts inspection only; stock movement must go through the corresponding receipt, picking, transfer, or dispatch workflow.

The inventory expiry sweep runs daily at 01:30 in `Asia/Ho_Chi_Minh`, updates expired batches, writes `BatchExpired` outbox events, and dispatches idempotent notifications to active Admin and assigned Warehouse Keeper accounts. Admin can inspect operational health through `GET /api/v1/inventory/expiry/health`; expiry-run entities are queryable below GraphQL `entities`. Configure the outbox worker with `BackgroundJobs:InventoryOutbox`.

Internal warehouse transfers are outside the registered MVP. Their legacy persistence/application code is retained for migration compatibility, but REST and GraphQL contracts are disabled and the development seeder no longer creates transfer data.

The development catalog dump contains:

| Data | Seeded examples |
| --- | --- |
| Categories | `CAT-01` through `CAT-04`, with Vietnamese names, a parent-child relationship, and an inactive category |
| Products | `SKU001` through `SKU005`, with Vietnamese names and units covering Draft, Published, Inactive, and replacement states |
| Prices | Multiple VND price-history entries for suggestion and effective-price testing |
| Warehouses | One central warehouse and two regional warehouses with Vietnamese addresses, coordinates, and minimum-stock monitoring thresholds |
| Transit time | Versioned per-warehouse distance bands with handling/transit time and estimated VND cost |
| MLOR | Retailer Category rule plus an SKU override, with effective version/audit metadata |
| Configuration | Allocation weights `0.5 / 0.3 / 0.2`, 20 km radius, 25% default MLOR, zero-carton order buffer, and one-carton order step; obsolete regional-transfer minimum stock is inactive |
| Regions | Hồ Chí Minh, Bình Dương, and an inactive Long An example |
| Sales groups | `HCM-A`, `HCM-B`, `BD-A`, and inactive `LA-A`, all with Vietnamese names |
| Assignment history | Current and previous Sales/retailer territory assignments for transfer testing |
| Promotions | Two Vietnamese campaigns (`KM-001`, `KM-002`) and one near-expiry batch discount (`KM-003`) |
| Assisted order | One idempotent Vietnamese `Approved` order for retailer `10004127`, including price/allocation snapshots, an active reservation, and an open Thủ Đức picking list |
| Picking QR | `TEM-000208` labels the seeded Thủ Đức `SKU001` batch and can demonstrate partial-label splitting during picking |
| Delivery | `NG-010`–`NG-012` and three Vietnamese warehouse vehicles support order dispatch demonstrations |

Approved orders create warehouse picking lists. Completing every list prepares the order for dispatch; Admin or Operator then assigns an active same-warehouse driver and vehicle, atomically issues reserved inventory, snapshots the delivery details, and changes the order and picked QR labels to delivery state. The Retailer linked to the order must scan every dispatched QR label exactly once to confirm receipt and move the order to `Delivered`. Dispatch and receipt commands require idempotency keys and expected order versions.

Final allocation approval creates one canonical payment ledger from the approved amount. It records confirmed and outstanding values for PayOS, manual prepaid, COD, and deferred collection under the same database lock and cap. Prepaid PayOS checkout is unavailable before that final snapshot, and dispatch remains blocked until the ledger is fully paid. A signed, amount/order/link/currency-validated PayOS webhook confirms the effect once; browser return URLs never mark an order paid. COD is confirmed by Admin or Operator only after complete delivery, while a deferred due date is set from the Vietnam complete-receipt date plus the snapshotted 7, 14, or 30-day term. Receipt never auto-settles COD or prepaid orders. Only full collection is supported.

`GET /api/v1/manual-payments/{id}` and `GET /api/v1/manual-payments?salesOrderId=...` provide use-case-shaped reads; scoped payment entities are also available below GraphQL `entities`. Manual writes require Admin or Operator; creation requires `Idempotency-Key`, while confirm/cancel commands require the current manual-payment version. Payment reads expose `amount`, `confirmedAmount`, and `outstandingAmount` without exposing provider secrets or raw webhook payloads.

F75 Sales KPI is exposed by `GET /api/v1/reports/sales-kpis` and the GraphQL `salesKpis` query. Admin and Operator may view all Sales or filter by Sales-group/user GUID; Sales Managers are restricted to their active groups, and Sales can only read their own KPI. Results include the configuration version, period, targets, weights, actual values, completion rates, weighted score, adjusted recognized revenue, and contributing order-GUID drill-down. Admin configures the versioned `reporting.sales_kpi` object through `/api/v1/configurations`; development seed supplies the approved monthly defaults. Revenue uses delivered orders, the delivery date (with Vietnam-local server-received fallback), and the canonical adjusted payment amount without recomputing historical promotion or HSD prices.

F80 in-app notifications are recipient-scoped for every authenticated role. `GET /api/v1/notifications` supports read-state/type filters and pagination, `GET /api/v1/notifications/badges` returns the unread count, and `PATCH /api/v1/notifications/{notificationId}/read` plus `PATCH /api/v1/notifications/read-all` update server read state idempotently. The GraphQL `notifications` and `notificationBadges` fields use the same application service as the REST reads. Results include a related-entity GUID and server-provided deep link when the producing event has an addressable object; clients must still authorize the destination resource when opening that link.

Configure `PayOS:ClientId`, `PayOS:ApiKey`, `PayOS:ChecksumKey`, `PayOS:ReturnUrl`, and `PayOS:CancelUrl` through user secrets or environment variables. Register the public `POST /api/v1/payments/payos/webhook` URL in the payOS dashboard; the endpoint accepts anonymous delivery but verifies every payload signature before changing payment state.

Use [the HTTP request collection](src/FSCM.API/FSCM.API.http) after seeding to log in, exercise receipt and QR workflows, inspect inventory/expiry health, query the catalog, and request a historical-price suggestion.

## The daily allocation run

Orders are not allocated when they arrive. A background job opens one run per Vietnam business date
at the cutoff, draws every eligible order from one shared stock pool, and puts the result to an
Admin as a proposal. Scope V10 (locked 22-23/10/2026) settles the rules below; several of them exist
to stop a tempting optimisation, so they are stated as rules rather than left to be inferred.

### Pipeline

| Step | Entry point | What it settles |
| --- | --- | --- |
| Open the run | `AllocationRunService.StartAsync(cutoffAt)` | One run per business date, snapshotting eligible orders and a stock pool |
| Choose batches | `AllocationPlanner` over `AllocationEngine` | Which batch serves which line, from the shared pool |
| Price the plan | `AllocationPricingService` | One pricing moment per plan version, shelf-life reduction per part |
| Record the working | `AllocationRunService.PersistTrace` | Why each batch was kept or ruled out, and how the survivors ranked |
| Approve | `AllocationPlanApprovalService` | Reserves stock, then marks the version approved |
| Issue picking | `RunPickingIssueService` | Gather per warehouse, then split back to order lines |
| Check the order may leave | `ShipmentService.GetOrderReadinessAsync` | Every warehouse part picked, payment condition met |
| Confirm it left | `ShipmentService.ConfirmIssueAsync` | OnHand and Reserved both fall; the hold is consumed |

### The run day

One published schedule, `eta.schedule`, is the only timetable. The ETA is built from it, the cutoff
job reads its cutoff before every wait, and the portal shows it. Recommended times:

| Milestone | Default | Meaning |
| --- | --- | --- |
| `sales_submission_advice` | 11:00 | **Advice, not a gate.** An order accepted after it and before the cutoff is still in the run |
| `order_review_reminder` | 11:30 | Orders still waiting for review will miss the cutoff |
| `allocation_run` | 00:00 | The cutoff: the run takes what was accepted and reviewed before it |
| `plan_approval_reminder` | 13:30 | A proposal is still undecided |
| `plan_approval_deadline` | 14:00 | A decision is expected. **Nothing approves itself after it** |

**The day closes at midnight.** The run allocates what came in during the day that has just ended,
which is why the advice and the review reminder read as 11:00 and 11:30 *of that day* while the
approval milestones belong to the day the run is on. The five stored values are times of day; each
one is placed around the cutoff, so a milestone that belongs before it and would otherwise land
after it moves back a day, and one that belongs after it moves forward. A midday cutoff, where all
five sit on the business date itself, resolves exactly as it always did.

That placement is also what makes a midnight cutoff expressible at all. Compared as plain times of
day, nothing can precede 00:00, so the rule "the reminders must fall before the cutoff" rejected
every schedule that closed the day at midnight - including one whose advice sat perfectly sensibly
at 11:00 the morning before. The ordering is now checked on the resolved instants, which is the
rule that was always meant.

Reminders are still validated against what they remind about: the advice comes before the review
reminder, both come before the cutoff, and the plan reminder falls between the cutoff and the
deadline. A schedule published before the reminders existed still loads, falling back to the
defaults above.

The run job is **on unless switched off**: `AllocationRunOptions.Enabled` defaults to true, so a
configuration file that omits `BackgroundJobs:AllocationRun` entirely still runs the job at the
cutoff. Anyone whose local `appsettings.json` predates that key should copy it across from the
example, and disable it in `appsettings.Development.json` while pointing at a shared database -
otherwise simply leaving the API running past the cutoff opens a run and writes to it.

`BackgroundJobs:AllocationRun:CutoffLocalTime` is only a fallback for a deployment with no published
schedule. Two independent cutoffs would let the ETA promise one time while the job keeps another.

A decision taken after the deadline is still a decision someone makes, but the plan was estimated
from the run moment, so a late approval needs its ETA re-checked.

### Running the run by hand

`POST /allocation-runs` (Admin) closes and plans today's run on demand. The clock is the normal
path; it was also the only one, which means a deployment that was down at noon could not produce
the day's plan at all - the orders wait for tomorrow and queue behind a day of newer ones.

The cutoff is not a parameter by default: it is read from the published schedule, so pressing the
button cannot quietly change which orders belong to the run. An Admin may pass a later moment on
the same business date, for the case where the run was down while orders kept arriving, and the
moment actually used is stored on the run - the boundary that was applied stays readable instead of
being inferred from when someone pressed it.

Three refusals, and they are checked in this order on purpose. A cutoff on any day but today is
refused first (`validation_error`), because the published time it would otherwise be measured
against is today's: yesterday's cutoff is earlier than it and tomorrow's is later than now, so
either would be reported as a time out of range when the caller actually named the wrong day. Then
a cutoff still in the future (`conflict`), and last one earlier than the published time
(`validation_error`), because moving it back would drop orders that belong to this run. Times in
these messages are written in Vietnam time whatever offset they arrived in.

Opening is idempotent on the business date, so pressing twice joins the run that already exists
rather than splitting the day across two plans. That also means the button cannot repair a run that
already closed on the wrong orders: the snapshot is taken once, and a later cutoff does not re-take
it.

Two traps live under this, both found by running the chain against PostgreSQL and both invisible to
the unit tests:

- **The cutoff must reach the database in UTC.** Npgsql binds only a `DateTimeOffset` whose offset
  is zero, and every caller builds the cutoff in Vietnam time - the job from the local clock, the
  endpoint from the schedule. `StartAsync` normalises to UTC, the same instant, before anything
  reaches a query; the business date is still read in Vietnam, where the day is defined. A
  Vietnam-offset value throws on the first real run, which is why a dev database can sit with an
  empty `ALLOCATION_RUN` while every test passes.
- **The persist step needs a tracked view.** Planning does not only insert rows: it moves the run to
  `AwaitingApproval`, marks each order fulfilled or short, records what each line drew and draws the
  pool down, all by assigning to the objects in the view it loaded. Read that view with
  `AsNoTracking` and the inserts land while every update is silently discarded, leaving the run at
  `Snapshotted` with its plan version already written. `FindViewForUpdateAsync` exists for this.

Neither is reachable from a unit test: the fake store returns live in-memory objects, which mutate
perfectly well, and holds no opinion about what Npgsql will bind.

### Rules that are easy to get wrong

- **Hours, never days.** Shelf life and MLOR are compared in hours against the product's total shelf
  life. A run arriving next morning is not the same as one arriving that evening, and rounding to
  days hides it.
- **An order is all or nothing at the door too.** Every warehouse part must be picked, and the
  payment condition met, before any part leaves. One warehouse short or damaged stops the whole
  order rather than sending what happens to be ready and leaving the retailer to chase the rest.
  The trap here is that **the shipment rows are not the list of warehouses an order is waiting
  on**: a part is created only when its warehouse closes its picking sheet, so a warehouse still
  picking has no row at all. Readiness therefore reads the expected warehouses from the sheets the
  approved plan produced (`ListExpectedWarehousesAsync`) and reports a warehouse with no part yet
  as one still to come, with a null shipment id. Deciding it from the rows alone made the first
  warehouse to finish look like the whole order, and issuing is gated on the same answer - which is
  partial delivery, arrived at by accident.
- **Stock moves once, at issue.** Approval raises Reserved and leaves OnHand alone; issuing takes
  both down together and consumes the hold that paid for it. Confirming twice returns the part
  unchanged, because a duplicated deduction stays invisible until someone counts the shelf.
- **An order is all or nothing.** A line that cannot be filled completely undoes every draw the
  order already made, including the lines that succeeded, and hands the quantity back to the pool
  for the orders behind it. There is no partial delivery for the run to decide on.
- **Batches first, price after.** A near-expiry reduction never reaches the scoring. Letting a
  discount raise a batch's score would let the cheaper batch win and then justify its own discount.
- **No batch moves between orders.** An order that has been given stock is never moved aside to
  serve a later one, so first come really is first served and a plan reads top to bottom.
- **Approval is never automatic.** A proposal past its reminder is still a proposal. Nothing is
  reserved until an Admin approves, and a recalculation supersedes a version rather than editing it.
- **A snapshot is not re-resolved.** MLOR, base price and shelf life are frozen on the run line. A
  configuration change mid-run must not restate a plan that is already being scored.

### Reading a run back

Two records are written per plan version and keyed alike, so they can be read side by side:

| What did the run decide about my order? | `ALLOCATION_RUN_ORDER` + `ALLOCATION_RUN_LINE` + the line trace | `GET /allocation-runs/orders/{salesOrderId}/result` |

The result is published only once a plan version is approved, and it states two things as fields
rather than prose: a short order **holds no stock**, and **no order can be resent keeping its place
in the queue**. Buying the rest means a new order, timestamped on arrival, queued behind whatever
came in before it. A screen that cannot read those two facts will eventually offer a button the run
cannot honour.

| Question | Record | Endpoint |
| --- | --- | --- |
| What was it priced at, and why that reduction? | `ALLOCATION_PLAN_PRICE` | `GET /allocation-runs/orders/{salesOrderId}/price` |
| Why those batches, and why not the others? | `ALLOCATION_LINE_TRACE` + `ALLOCATION_TRACE_CANDIDATE` + `ALLOCATION_TRACE_SCORE` | `GET /allocation-runs/orders/{salesOrderId}/trace` |

Both carry the plan version's status. A price or a trace from a version still awaiting approval is a
proposal, not a settled outcome, and a caller that cannot tell the two apart will eventually collect
against a quotation.

The trace keeps rejected candidates and losing scores on purpose: "why not that batch" cannot be
answered from the chosen ones, and a score means nothing without the scores it beat. A line's
outcome distinguishes `Released` - it drew its stock and lost it to a later line's shortfall - from
`Short`, the line that actually failed.

## Closing an allocation run (F95)

An Admin closes an approved run with
`POST /api/v1/allocation-runs/{runId}/close`, passing the current `expectedVersion`
and an optional reason. Closing changes only the run workflow state to `Closed`;
reservations, approved plan rows, prices and picking history remain immutable.
The action is recorded as `AllocationRunClosed` in the existing `AuditLog` table,
so no migration is needed. A run that is still awaiting approval, planning,
failed or already closed cannot create a second close audit.

## Cancelling an order that is already holding stock (F94)

Two models in this schema can hold stock, and only one of them is still written:

| Holder | Reservation link | Written by |
| --- | --- | --- |
| `Allocation` (per order line) | `Reservation.AllocationId` | nothing any more; rows survive from before the daily runs |
| `AllocationRunAllocation` | `Reservation.AllocationRunAllocationId` | every run since |

An order is not moved to a new status when its run is approved - it stays `PendingAllocation` while
the plan is approved, priced and reserved - so cancelling is allowed for the whole of that window,
exactly as F94 intends. The release has to find the stock through the run:

```
SALES_ORDER -> ALLOCATION_RUN_ORDER -> ALLOCATION_RUN_LINE -> ALLOCATION_RUN_ALLOCATION -> RESERVATION
```

`OrderReleaseService` walks that path, sets each active reservation to `Released`, takes the
quantity off `ReservedQuantity`, recomputes `AvailableQuantity` and writes an `OrderRelease` row to
the stock ledger. It runs **inside the cancellation's transaction**: an order marked cancelled whose
stock is still held would be invisible to the next run, with no screen left that could explain the
missing quantity.

### Taking the order off the picking sheets

A sheet is one warehouse's work for one *run*, shared by every order in it, so a cancellation
reduces the gather row by this order's share and removes only a gather that has nothing left to
collect. The other orders on the sheet are untouched.

Cancelling is refused once a warehouse has started - any of this order's splits with
`PickedQuantity > 0`, or a sheet past `Issued`. Someone is standing at a shelf with that sheet in
hand, and deleting their rows under them is how a picked carton goes missing. The sheet is checked
**before** any stock moves, so a refused cancellation leaves the hold exactly as it was.

### Rules that are easy to get wrong

- **An order holding nothing is not an error.** One cancelled before it ever reached a run, and a
  cancellation sent twice, both arrive here and release nothing.
- **Only active reservations.** One already released or consumed is not ours to touch, which is what
  makes a second cancellation a no-op rather than a double release.
- **A balance holding less than the order claims is refused, not clamped.** No sequence of releases
  produces it; writing a negative reservation would bury it.
- **Everything is read tracked.** `AsNoTracking` here would make every release a silent no-op.

## Offline mobile snapshot and manual send

Sales devices explicitly request `GET /api/v1/mobile/snapshot`. The response is scoped from the
authenticated Sales assignment and includes an opaque content-derived `version`, a checksum, and a
full payload (`deltaMode=Full`). The client revalidates its cached version with `If-None-Match`; the
server returns `304 Not Modified` when the scoped payload is unchanged. Full-only synchronization is
intentional so assignment removal, deactivation, and relationship deletion cannot leave stale rows
in SQLite. Cached inventory is
indicative only; cached base price, ordinary promotion, and MLOR conditions are revalidated by the
server. The snapshot never promises a batch-specific shelf-life discount.

An offline draft remains local to the mobile client. When connectivity is available, Sales presses
Send and calls `POST /api/v1/orders/sync` (the compatibility route `/api/v1/orders/offline` has the
same behavior). A transport retry reuses `clientOrderKey` and returns the existing order. If base
price or MLOR changed, the API returns `409` with the current conditions and creates no order; the
client must show those changes and resend with `confirmConditionChanges=true`. Server receipt time
sets cutoff priority. Sending never reserves stock and is not rejected merely because indicative
stock is short.

The snapshot GET intentionally has no GraphQL twin: it is a bulk device-cache synchronization
transport with HTTP ETag/checksum semantics, not a persisted business read model. Order reads remain
available via the shared REST/GraphQL order read services.

## Product ranking (F78)

`GET /api/v1/reports/best-selling-products` and GraphQL `bestSellingProducts` rank delivered products for an
inclusive Vietnam-local period by either `Quantity` or `Revenue`. Quantity is normalized to the
canonical order unit `carton`. Revenue starts from the approved allocation-fragment net price and
allocates approved order-level adjustments proportionally to each product. Admin/Operator may use
all or a selected Sales group; Sales Manager/Sales access is restricted to active group membership.

## Promotion and complaint fixes (CH-08 / CH-13)

- Ordinary campaign minimum quantity is aggregated across matching SKU/category lines in one order only; quantities never combine across orders.
- The selected campaign is deterministic: highest percentage wins, then promotion code breaks ties. The chosen discount is snapshotted on each order line; allocation pricing remains the source of truth for batch/HSD pricing.
- Complaints retain the shipment part, allocation plan price, frozen final unit price and calculated adjustment for the affected quantity. The API accepts `shipmentId` and `allocationPlanPriceId` and validates that they belong to the received batch; evidence remains attached to the same discrepancy item.

## Validation

```powershell
dotnet restore FSCM.slnx
dotnet build FSCM.slnx --configuration Release --no-restore
dotnet test FSCM.slnx --configuration Release --no-build
```

## Delivery

GitHub Actions validates pushes and pull requests. A separate workflow mirrors every GitHub branch to the `FSCM-Backend` Azure Repo. Azure Pipelines builds, tests, packages, and deploys `main` to the configured Azure App Service.
