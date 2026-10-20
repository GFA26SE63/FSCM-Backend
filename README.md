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
│  ├─ GraphQL/           read-only Query surface, type extensions and DataLoaders
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
REST Controller / GraphQL Query
  → Application Feature Service
  → Application store/gateway interface
  → Persistence store or Infrastructure adapter
  → PostgreSQL / external service
```

Controllers and GraphQL resolvers must stay thin: bind input, resolve actor identity/role, call one application use case, and map the transport result. Business validation, state transitions, idempotency, concurrency, allocation, pricing, and scope enforcement belong in Application/Domain. Persistence may optimize queries but must not become the owner of business policy.

### Adding or extending a feature

1. Put domain state/constants in `FSCM.Domain` only when they are independent of transport and storage.
2. Add contracts, operation exception, store/gateway port, and service under one `Application/Features/<Feature>` folder.
3. Implement database access under `Persistence/<Feature>` or third-party access under `Infrastructure/<Feature>`; register it in that project's `DependencyInjection.cs`.
4. Add REST request DTOs and a controller under `API`. Reads that clients need to compose may also be added to `SystemQuery`; GraphQL remains query-only unless the architecture is deliberately changed.
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

### Identity, catalog, warehouse configuration, and development data

BE-01 uses Entra ID only to authenticate Portal sign-in. The Portal sends its Entra bearer token to `POST /api/v1/auth/entra/exchange`; the API validates tenant, audience, signature and lifetime, matches a pre-provisioned FSCM account by `oid`/email, and returns the same FSCM JWT/refresh-token contract used by the rest of the system. Protected REST and GraphQL operations accept only the internal FSCM JWT. Mobile continues to use `POST /api/v1/auth/login`; Google sign-in is not enabled.

Configure `Entra:TenantId`, `Entra:ClientId`, `Jwt:Issuer`, `Jwt:Audience`, and a JWT secret of at least 32 UTF-8 bytes through environment variables, user secrets, or the deployment secret store. No credential is committed in `appsettings.json`. `/api/v1/auth/me` returns the current role plus warehouse, region, Sales-group, and Retailer scopes. Every request revalidates that the FSCM account is active and that the token role still matches the database, so status/role changes take effect without waiting for token expiry.

Admin user management is available under `/api/v1/users`: filtered deterministic paging, detail/profile updates, role/status commands, and warehouse/region/Sales-group/Retailer scope assignment. `GET` reads have equivalent `users`, `user`, `userScope`, and `me` GraphQL fields. User creation accepts `identityProvider` as `Local` or `Entra`; Entra accounts are pre-provisioned and bind their immutable `oid` on first exchange, while Local accounts use the activation flow. Admin creation and promotion remain blocked in the public API.

Create the first deployment Admin exactly once from the deployment secret store:

```powershell
dotnet run --project src/FSCM.API/FSCM.API.csproj -- --bootstrap-admin
```

Supply `AdminBootstrap:Email`, `AdminBootstrap:FullName`, `AdminBootstrap:IdentityProvider`, and either `AdminBootstrap:EntraObjectId` or `AdminBootstrap:Password`. The command applies pending migrations, exits without changing an existing Admin, and never logs the password.

Create the development-only identity dataset after applying migrations:

```powershell
dotnet user-secrets set "SeedData:DefaultPassword" "FscmDemo#2026" --project src/FSCM.API/FSCM.API.csproj
dotnet run --project src/FSCM.API/FSCM.API.csproj --launch-profile http -- --seed
```

The command is idempotent and creates active users for all six roles plus BE-02 catalog dump data. The example password above is for local development only and must not be used in a deployed environment.

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

BE-03 exposes warehouse master data and coordinates under `/api/v1/warehouses`, per-warehouse ETA rules under `/api/v1/warehouses/{warehouseId}/transit-time-rules`, retailer-product MLOR under `/api/v1/mlor-configurations`, and versioned business settings under `/api/v1/configurations`. Authenticated users can read current data and history; only Admin can mutate it. Warehouse records are inactivated instead of deleted, and each configuration update preserves the previous version.

BE-04 exposes region and Sales-group master data under `/api/v1/regions` and `/api/v1/sales-groups`. Admin and scoped Sales Managers can manage Sales and Retailer assignments under `/api/v1/territory-assignments`; every transfer closes the previous row and creates a new row so assignment history remains intact. Sales order eligibility is checked under `/api/v1/territory-access/retailers/{retailerId}/order-eligibility`. Only an active Sales user whose current group owns the Retailer in the same region is eligible. Later orders retain `CreatedByUserId` and `SalesGroupId` snapshots even after reassignment.

BE-09 exposes campaign management under `/api/v1/promotions`, shelf-life suggestions and batch assignments under `/api/v1/batch-discounts`, and deterministic price snapshots under `/api/v1/pricing/snapshot`. Admin manages campaigns and batch discounts; authenticated users can read applicable offers and calculate prices. Campaign and batch discounts do not compound: the highest eligible rate wins, an equal batch-specific rate wins over a campaign, and promotion code is the final stable tie-breaker. Every REST GET has an equivalent GraphQL query at `/graphql`.

Assisted ordering is exposed under `/api/v1/orders`. Sales can create an order only for an active retailer in their current territory; the server validates carton steps, calculates the final promotion, allocates eligible batches, stores the scoring trace, and holds inventory atomically. Operator approval converts holds to reservations, rejection releases them, and cancellation is restricted to the owning Sales user while pending or to Operator/Admin. Scoped list, detail, and allocation-trace reads are also available through GraphQL.

Order picking is exposed under `/api/v1/picking-lists`. Approval creates one immutable-code list per allocated warehouse (`{OrderCode}A`, `{OrderCode}B`, ...). An assigned Warehouse Keeper scans only available QR labels for the exact allocated warehouse, batch, and SKU. Partial scans create child labels while preserving lineage; every scan is idempotent and version checked. Completing a list issues its reservations, and completing every warehouse list moves the order to `Issued`. REST GET operations have equivalent scoped GraphQL queries.

The development catalog dump contains:

| Data | Seeded examples |
| --- | --- |
| Categories | `CAT-01` through `CAT-04`, with Vietnamese names, a parent-child relationship, and an inactive category |
| Products | `SKU001` through `SKU005`, with Vietnamese names and units covering Draft, Published, Inactive, and replacement states |
| Prices | Multiple VND price-history entries for suggestion and effective-price testing |
| Warehouses | One central warehouse and two regional warehouses with Vietnamese addresses and coordinates |
| Transit time | Per-warehouse rules for up to 10 km in one day and up to 20 km in two days |
| MLOR | Retailer-product rules for `SKU001` and `SKU002` |
| Configuration | Allocation weights `0.5 / 0.3 / 0.2`, 20 km radius, 35-carton threshold, 25% default MLOR, zero-carton order buffer, and one-carton order step |
| Regions | Hồ Chí Minh, Bình Dương, and an inactive Long An example |
| Sales groups | `HCM-A`, `HCM-B`, `BD-A`, and inactive `LA-A`, all with Vietnamese names |
| Assignment history | Current and previous Sales/retailer territory assignments for transfer testing |
| Promotions | Two Vietnamese campaigns (`KM-001`, `KM-002`) and one near-expiry batch discount (`KM-003`) |
| Assisted order | One idempotent Vietnamese `Approved` order for retailer `10004127`, including price/allocation snapshots, an active reservation, and an open Thủ Đức picking list |
| Picking QR | `TEM-000208` labels the seeded Thủ Đức `SKU001` batch and can demonstrate partial-label splitting during picking |
| Delivery | `NG-010`–`NG-012` and three Vietnamese warehouse vehicles support order dispatch demonstrations |

Approved orders create warehouse picking lists. Completing every list issues inventory and moves the order to `Issued`. Admin or Operator can then assign an active same-warehouse driver and vehicle, which snapshots their details and changes the order and picked QR labels to delivery state. The Retailer linked to the order must scan every dispatched QR label exactly once to confirm receipt and move the order to `Delivered`. Dispatch and receipt commands require idempotency keys and expected order versions.

Retailer receipt also creates one payment snapshot per order. COD and prepaid orders settle automatically on delivery. Credit orders create an unpaid receivable whose due date is the Vietnam delivery date plus the Retailer's 7, 14, or 30-day term. Sales may record one full cash collection for their own orders; Operator may record one full bank-transfer collection. A scoped user can also create a payOS VietQR checkout for an unpaid Credit receivable; its signed webhook records the payment as `BankTransfer` and keeps the payOS link/reference for reconciliation. Once a payOS checkout exists, manual collection is blocked to prevent duplicate settlement. Payment reads expose effective overdue status without implementing out-of-scope partial payments, credit limits, or debt-aging reports.

Configure `PayOS:ClientId`, `PayOS:ApiKey`, `PayOS:ChecksumKey`, `PayOS:ReturnUrl`, and `PayOS:CancelUrl` through user secrets or environment variables. Register the public `POST /api/v1/payments/payos/webhook` URL in the payOS dashboard; the endpoint accepts anonymous delivery but verifies every payload signature before changing payment state.

Use [the HTTP request collection](src/FSCM.API/FSCM.API.http) after seeding to log in, query the catalog, inspect price history, and request a historical-price suggestion.

## Validation

```powershell
dotnet restore FSCM.slnx
dotnet build FSCM.slnx --configuration Release --no-restore
dotnet test FSCM.slnx --configuration Release --no-build
```

## Delivery

GitHub Actions validates pushes and pull requests. A separate workflow mirrors every GitHub branch to the `FSCM-Backend` Azure Repo. Azure Pipelines builds, tests, packages, and deploys `main` to the configured Azure App Service.
