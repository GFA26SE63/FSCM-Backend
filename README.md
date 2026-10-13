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

### Identity, catalog, and development data

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
| SalesManager | `sales.manager@fscm.local` | `SG-HCM-01`, `RTL-HCM-001` |
| Sales | `sales@fscm.local` | `SG-HCM-01`, `RTL-HCM-001` |
| WarehouseKeeper | `warehouse.keeper@fscm.local` | `WH-HCM-01` |
| Retailer | `retailer@fscm.local` | `SG-HCM-01`, `RTL-HCM-001` |

Admin accounts are bootstrap-only: the user-management API rejects both Admin creation and Admin role promotion.

BE-02 exposes authenticated category and product reads under `/api/v1/categories` and `/api/v1/products`. Admin-only commands manage category relationships, Draft products, publication, inactivation, replacement SKUs, and effective price history. Publishing a replacement SKU automatically inactivates its predecessor. Published product master data is immutable; price changes are recorded as history instead of overwriting prior values.

The development catalog dump contains:

| Data | Seeded examples |
| --- | --- |
| Categories | `CAT-01` through `CAT-04`, with Vietnamese names, a parent-child relationship, and an inactive category |
| Products | `SKU001` through `SKU005`, with Vietnamese names and units covering Draft, Published, Inactive, and replacement states |
| Prices | Multiple VND price-history entries for suggestion and effective-price testing |

Use [the HTTP request collection](src/FSCM.API/FSCM.API.http) after seeding to log in, query the catalog, inspect price history, and request a historical-price suggestion.

## Validation

```powershell
dotnet restore FSCM.slnx
dotnet build FSCM.slnx --configuration Release --no-restore
dotnet test FSCM.slnx --configuration Release --no-build
```

## Delivery

GitHub Actions validates pushes and pull requests. A separate workflow mirrors every GitHub branch to the `FSCM-Backend` Azure Repo. Azure Pipelines builds, tests, packages, and deploys `main` to the configured Azure App Service.
