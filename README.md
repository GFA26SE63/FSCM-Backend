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

Assisted ordering is exposed under `/api/v1/orders`. Sales can create an order only for an active retailer in their current territory; the server snapshots the exact base-price record/effective window, ordinary-campaign conditions, and the resolved Retailer MLOR source/scope/version for every line, then marks pricing pending the final batch-derived HSD calculation. Capture does not query or select inventory, create an allocation, hold/reserve stock, or collect payment. `GET /api/v1/orders` supports scoped `createdByMe`, status, retailer, search, and deterministic paging; scoped order entities, lines, history, and shipment links are available below GraphQL `entities`. The legacy approve/reject contract remains pending the separately tracked F36 rewrite.

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

Configure `PayOS:ClientId`, `PayOS:ApiKey`, `PayOS:ChecksumKey`, `PayOS:ReturnUrl`, and `PayOS:CancelUrl` through user secrets or environment variables. Register the public `POST /api/v1/payments/payos/webhook` URL in the payOS dashboard; the endpoint accepts anonymous delivery but verifies every payload signature before changing payment state.

Use [the HTTP request collection](src/FSCM.API/FSCM.API.http) after seeding to log in, exercise receipt and QR workflows, inspect inventory/expiry health, query the catalog, and request a historical-price suggestion.

## The 12:00 allocation run

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

### The run day

One published schedule, `eta.schedule`, is the only timetable. The ETA is built from it, the cutoff
job reads its cutoff before every wait, and the portal shows it. Recommended times:

| Milestone | Default | Meaning |
| --- | --- | --- |
| `sales_submission_advice` | 11:00 | **Advice, not a gate.** An order accepted after it and before the cutoff is still in the run |
| `order_review_reminder` | 11:30 | Orders still waiting for review will miss the cutoff |
| `allocation_run` | 12:00 | The cutoff: the run takes what was accepted and reviewed before it |
| `plan_approval_reminder` | 13:30 | A proposal is still undecided |
| `plan_approval_deadline` | 14:00 | A decision is expected. **Nothing approves itself after it** |

Reminders are validated against what they remind about: the two before the cutoff must fall before
it, and the plan reminder must fall between the cutoff and the deadline. A schedule published before
the reminders existed still loads, falling back to the defaults above.

The run job is **on unless switched off**: `AllocationRunOptions.Enabled` defaults to true, so a
configuration file that omits `BackgroundJobs:AllocationRun` entirely still runs the job at 12:00.
Anyone whose local `appsettings.json` predates that key should copy it across from the example, and
disable it in `appsettings.Development.json` while pointing at a shared database - otherwise simply
leaving the API running past noon opens a run and writes to it.

`BackgroundJobs:AllocationRun:CutoffLocalTime` is only a fallback for a deployment with no published
schedule. Two independent cutoffs would let the ETA promise one time while the job keeps another.

A decision taken after the deadline is still a decision someone makes, but the plan was estimated
from the run moment, so a late approval needs its ETA re-checked.

### Rules that are easy to get wrong

- **Hours, never days.** Shelf life and MLOR are compared in hours against the product's total shelf
  life. A run arriving next morning is not the same as one arriving that evening, and rounding to
  days hides it.
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

## Offline mobile snapshot and manual send

Sales devices explicitly request `GET /api/v1/mobile/snapshot`. The response is scoped from the
authenticated Sales assignment and includes an opaque `version`, a checksum of the current full
snapshot, and either a full payload or a delta when `sinceVersion` is supplied. Cached inventory is
indicative only; cached base price, ordinary promotion, and MLOR conditions are revalidated by the
server. The snapshot never promises a batch-specific shelf-life discount.

An offline draft remains local to the mobile client. When connectivity is available, Sales presses
Send and calls `POST /api/v1/orders/sync` (the compatibility route `/api/v1/orders/offline` has the
same behavior). A transport retry reuses `clientOrderKey` and returns the existing order. If base
price or MLOR changed, the API returns `409` with the current conditions and creates no order; the
client must show those changes and resend with `confirmConditionChanges=true`. Server receipt time
sets cutoff priority. Sending never reserves stock and is not rejected merely because indicative
stock is short.

The snapshot GET intentionally has no GraphQL twin: it is a device cache synchronization transport
with delta/checksum semantics, not a persisted business read model. Order reads remain available via
the shared REST/GraphQL order read services.

## Validation

```powershell
dotnet restore FSCM.slnx
dotnet build FSCM.slnx --configuration Release --no-restore
dotnet test FSCM.slnx --configuration Release --no-build
```

## Delivery

GitHub Actions validates pushes and pull requests. A separate workflow mirrors every GitHub branch to the `FSCM-Backend` Azure Repo. Azure Pipelines builds, tests, packages, and deploys `main` to the configured Azure App Service.
