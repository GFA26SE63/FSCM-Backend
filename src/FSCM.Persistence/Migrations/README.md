# Database migrations

EF Core migrations in this directory are the version-controlled PostgreSQL schema history for FSCM.

`InitialPhysicalSchema` establishes the approved physical model: 57 tables covering identity and scope, catalog, retailers, warehouse inventory, sales orders, allocation, fulfillment, delivery, payment, loyalty, transfers, audit, idempotency, and outbox processing.

## Create a migration

Run from the backend repository root:

```powershell
dotnet ef migrations add <MigrationName> `
  --project src/FSCM.Persistence/FSCM.Persistence.csproj `
  --startup-project src/FSCM.API/FSCM.API.csproj `
  --context AppDbContext `
  --output-dir Migrations
```

Review the generated migration and model snapshot. Never edit only the snapshot.

## Apply migrations

Configure `ConnectionStrings:DefaultConnection` through ignored local settings, user secrets, or environment variables, then run:

```powershell
dotnet ef database update `
  --project src/FSCM.Persistence/FSCM.Persistence.csproj `
  --startup-project src/FSCM.API/FSCM.API.csproj `
  --context AppDbContext
```

Production migrations must run through the controlled deployment process, not API startup.

## Generate an idempotent deployment script

```powershell
dotnet ef migrations script --idempotent `
  --project src/FSCM.Persistence/FSCM.Persistence.csproj `
  --startup-project src/FSCM.API/FSCM.API.csproj `
  --context AppDbContext `
  --output fscm-migration.sql
```

Do not commit generated deployment scripts containing environment-specific changes or credentials.
