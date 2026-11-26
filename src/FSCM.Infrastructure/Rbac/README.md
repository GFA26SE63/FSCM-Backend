# Dynamic RBAC contract

`USER_ACCOUNT.RoleDefinitionId` references `SYS_ROLE.Id` with restricted deletion.
`SYS_ROLE_PERMISSION.RoleId` holds that role's exact grants. Authentication loads
permissions through the account's foreign key on every HTTP request; JWTs never
carry authoritative grants. Empty permissions deny access and are never replaced
with role defaults during reads or edits.

`SYS_ROLE.BaseRole` defines the business data scope: Operator, WarehouseKeeper,
SalesManager, Sales, or Retailer. It is selected when a custom role is created
and remains immutable. `USER_ACCOUNT.Role` retains this business profile for
existing domain scope and workflow rules. A database trigger maintains the
profile and reference together, including legacy provisioning paths. The FK and
check constraints prevent unlinked accounts and custom Admin profiles.

`/api/v1/auth/me` returns `role` (business profile), `roleDefinitionId`, `roleCode`,
`roleName`, and `permissions`. Web and mobile retain these fields across session
refreshes. Role reassignment revokes existing refresh tokens; requests always
load the latest assigned grants.

## Authorization

- Business REST actions require both an appropriate screen grant and their
  exact `api:<Controller>.<Action>` grant. There are 284 registered operations.
  Unknown operations fail closed. Controllers use authenticated policies without
  hardcoded role allowlists.
- GraphQL authorizes each selected entity field, including aliases, before
  resolving it. Queries continue to apply server-side warehouse, territory,
  retailer and ownership filters. Shared warehouse endpoints additionally check
  the actual document's reason/type, and GraphQL filters document rows by grant.
- Domain state transitions and resource scopes remain authoritative after the
  permission check. A feature grant does not remove stock, ownership, financial,
  concurrency, or document-state requirements.
- Admin remains the protected full-access role. User management cannot create
  Admin users or assign a custom Admin business profile.

`ApiPermissionCatalog.InitialRoles` and `SystemRoleGrants` are initial provisioning
metadata only. Runtime authorization never consults their role allowlists.
Existing grants are maintained by role commands and are not re-seeded on reads.
Role create/update/delete operations write audit snapshots in the same SaveChanges
transaction as the permission change.

## Reads and commands

GraphQL `entities.roleReads`, `roleRead`, `screenFeatures`, `userReads` and
`userRead` provide typed role/user read contracts. The screen catalog includes
screen keys and API operations grouped by controller. Role CRUD and user role
assignment remain REST commands. The role editor exposes separate Web, Mobile,
and operation selections plus the business scope for custom roles.

## Database and validation

Runtime and EF tooling use only the ignored API `appsettings.json` connection.
`AppDbContextFactory` selects the same database; no alternate database or connection
override is used. The forward account-link migration backfills existing accounts,
initializes missing role definitions and API grants, and preserves existing screen
revocations. The following migration enforces account-link and profile constraints.
Neither migration resets data or runs the development seeder.

`ConfiguredDatabaseRbacTests` validates the actual configured database and production
IdentityStore, including a formerly Admin-only API granted to a custom warehouse
role and immediate revocation with the same JWT. Its own test records and audits
are rolled back in one transaction. API contract fixtures disable scheduled workers.
Legacy tests requiring a separate/resettable PostgreSQL database remain skipped;
they must not reset the configured shared database.
