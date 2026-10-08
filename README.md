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
dotnet run --project src/FSCM.API/FSCM.API.csproj --launch-profile https
```

The development profile exposes Swagger at `/swagger`, GraphQL at `/graphql`, and health checks at `/health`.

## Validation

```powershell
dotnet restore FSCM.slnx
dotnet build FSCM.slnx --configuration Release --no-restore
dotnet test FSCM.slnx --configuration Release --no-build
```

## Delivery

GitHub Actions validates pushes and pull requests. A separate workflow mirrors every GitHub branch to the `FSCM-Backend` Azure Repo. Azure Pipelines builds, tests, packages, and deploys `main` to the configured Azure App Service.
