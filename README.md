# FSCM Backend

[![Backend CI](https://github.com/GFA26SE63/FSCM-Backend/actions/workflows/backend-ci.yml/badge.svg)](https://github.com/GFA26SE63/FSCM-Backend/actions/workflows/backend-ci.yml)

.NET 10 API for the FSCM short shelf-life FMCG warehouse operations and distribution allocation system.

The solution follows Clean Architecture boundaries across API, Application, Domain, Infrastructure, Persistence, and Shared projects. It also contains unit, integration, and load-test projects.

## Related repositories

- [FSCM Frontend](https://github.com/GFA26SE63/FSCM-Frontend)
- [FSCM Mobile](https://github.com/GFA26SE63/FSCM-Mobile)

Cross-system architecture documentation is stored beside the local repositories under `FSCM/.docs` and `FSCM/diagram`.

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

BE-01 provides JWT login and refresh-token rotation, account activation, password reset, six-role authorization, and warehouse/sales-group/retailer scopes under `/api/v1/auth` and `/api/v1/users`.

Create the development-only identity dataset after applying migrations:

```powershell
dotnet user-secrets set "SeedData:DefaultPassword" "FscmDemo#2026" --project src/FSCM.API/FSCM.API.csproj
dotnet run --project src/FSCM.API/FSCM.API.csproj --launch-profile http -- --seed
```

The command is idempotent and creates active users for all six roles plus BE-02 catalog dump data. The example password above is for local development only and must not be used in a deployed environment.

| Role | Email | Seeded scope |
| --- | --- | --- |
| Admin | `admin@fscm.local` | System-wide administration |
| Operator | `operator@fscm.local` | `WH-HCM-01` |
| SalesManager | `sales.manager@fscm.local` | `HCM-A`, `RTL-HCM-001` |
| Sales | `sales@fscm.local` | `HCM-A`, `RTL-HCM-001` |
| WarehouseKeeper | `warehouse.keeper@fscm.local` | `WH-HCM-01` |
| Retailer | `retailer@fscm.local` | Retailer account only |

Admin accounts are bootstrap-only: the user-management API rejects both Admin creation and Admin role promotion.

BE-02 exposes authenticated category and product reads under `/api/v1/categories` and `/api/v1/products`. Admin-only commands manage category relationships, Draft products, publication, inactivation, replacement SKUs, and effective price history. Publishing a replacement SKU automatically inactivates its predecessor. Published product master data is immutable; price changes are recorded as history instead of overwriting prior values.

BE-03 exposes warehouse master data and coordinates under `/api/v1/warehouses`, per-warehouse ETA rules under `/api/v1/warehouses/{warehouseId}/transit-time-rules`, retailer-product MLOR under `/api/v1/mlor-configurations`, and versioned business settings under `/api/v1/configurations`. Authenticated users can read current data and history; only Admin can mutate it. Warehouse records are inactivated instead of deleted, and each configuration update preserves the previous version.

BE-04 exposes region and Sales-group master data under `/api/v1/regions` and `/api/v1/sales-groups`. Admin and scoped Sales Managers can manage Sales and Retailer assignments under `/api/v1/territory-assignments`; every transfer closes the previous row and creates a new row so assignment history remains intact. Sales order eligibility is checked under `/api/v1/territory-access/retailers/{retailerId}/order-eligibility`. Only an active Sales user whose current group owns the Retailer in the same region is eligible. Later orders retain `CreatedByUserId` and `SalesGroupId` snapshots even after reassignment.

BE-09 exposes campaign management under `/api/v1/promotions`, shelf-life suggestions and batch assignments under `/api/v1/batch-discounts`, and deterministic price snapshots under `/api/v1/pricing/snapshot`. Admin manages campaigns and batch discounts; authenticated users can read applicable offers and calculate prices. Campaign and batch discounts do not compound: the highest eligible rate wins, an equal batch-specific rate wins over a campaign, and promotion code is the final stable tie-breaker. Every REST GET has an equivalent GraphQL query at `/graphql`.

Assisted ordering is exposed under `/api/v1/orders`. Sales can create an order only for an active retailer in their current territory; the server validates carton steps, calculates the final promotion, allocates eligible batches, stores the scoring trace, and holds inventory atomically. Operator approval converts holds to reservations, rejection releases them, and cancellation is restricted to the owning Sales user while pending or to Operator/Admin. Scoped list, detail, and allocation-trace reads are also available through GraphQL.

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
| Assisted order | One idempotent Vietnamese `PendingApproval` order for retailer `10004127`, including price/allocation snapshots and an active stock hold |

Use [the HTTP request collection](src/FSCM.API/FSCM.API.http) after seeding to log in, query the catalog, inspect price history, and request a historical-price suggestion.

## Validation

```powershell
dotnet restore FSCM.slnx
dotnet build FSCM.slnx --configuration Release --no-restore
dotnet test FSCM.slnx --configuration Release --no-build
```

## Delivery

GitHub Actions validates pushes and pull requests. A separate workflow mirrors every GitHub branch to the `FSCM-Backend` Azure Repo. Azure Pipelines builds, tests, packages, and deploys `main` to the configured Azure App Service.
