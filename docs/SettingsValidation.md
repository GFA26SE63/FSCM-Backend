# Settings validation and effective dates

Business calendar dates use Vietnam time (UTC+07:00), regardless of the machine's time zone.

| Setting | When a new change may apply | Server safeguards |
|---|---|---|
| Shared transit table | Tomorrow or later; strictly after the newest published version | Contiguous non-overlapping bands from zero, serving-radius coverage, non-negative whole minutes, supported money precision, 500-character note |
| Shelf-life reduction table | Tomorrow or later; strictly after the newest published version | Exactly one SKU/category identifier per tier; published SKU or active category; non-overlapping tiers for a scope; percentage and money bounds/precision |
| Campaign | Today or later; today's start is clamped to save time, then activation time | No backdating; end after effective start; cannot activate at/after expiry; deactivate before editing an active campaign |
| Loyalty | New programs start today or later, clamped to save/activation time | Historical start cannot be rewritten after a program has begun. Rate updates remain supported and preserve the existing start; previously frozen earning rates stay unchanged |
| Allocation weights, radius, order step, default MLOR and ETA schedule | Server save time, with existing version history | JSON types, numeric bounds, exact allocation-weight sum, supported time zone, whole non-negative minutes and unique HH:mm dispatch slots |
| Warehouse/region | Immediate | Create/update/activate a warehouse only in an existing active region; cannot deactivate a region containing an active warehouse or sales group |

`current` table endpoints retain their existing meaning: the newest published version, including a scheduled version. Pricing and ETA lookups resolve the effective interval at the requested time, or server time when omitted. `IsActive` marks the open/latest version; a closed historical version can still be effective until its `EffectiveTo`. Portal labels distinguish scheduled versions.

Shared transit internal band upper bounds remain exclusive. If the inclusive serving radius is exactly the final table endpoint, that point resolves to the final band.

Invalid publish requests are rejected before changing the previous version. PostgreSQL unique-version/open-version constraints remain the final guard for concurrent publishers; a uniqueness collision is returned as a conflict requiring reload.

## Atomic group saves and account phones

- Portal saves the four immediate allocation/order settings through one `PUT /api/v1/configurations/batch` request. The entire group is validated before tracked records are changed, then saved in one PostgreSQL transaction. Unchanged retries do not create new versions; competing version writes return a conflict.
- `POST /api/v1/promotions/campaigns` validates every rule before staging records. `PerSku` creates one campaign per product with that product's own minimum quantity and discount; `ProductGroup` keeps a single shared threshold. Creation, optional activation, scope links and audit records commit together.
- A campaign request UUID identifies a submission. Portal retains it while retrying unchanged input; the server returns the saved IDs for the same actor/payload and rejects reuse with different data. The durable audit receipt still prevents duplicates after temporary request-key cleanup. Sequence numbers can have gaps after a rolled-back request.
- Account creation checks phone ownership before staging writes. `AddUniqueAccountPhone` adds a unique non-null phone index and canonical-value check, so concurrent writers cannot bypass the API check. Phone collisions return HTTP 409, including when the competing write reaches the DB first.
- The migration trims legacy values, releases a duplicate phone only from an inactive `@pending.fscm.local` Retailer placeholder with no retailer link, credential or refresh-token history, and only if a non-placeholder account holds the same phone. Accounts are retained and the old phone is recorded in an audit entry. Remaining ambiguous duplicates abort the whole migration. Rolling back the schema does not automatically restore released duplicate phones; the audit is retained for deliberate recovery.
- Apply migration `20261111120430_AddUniqueAccountPhone` before deploying the Backend, then deploy Portal with the new group endpoints. It was applied to the development PostgreSQL database after a read-only preflight: three orphan placeholders were audited, no duplicate groups remained, and the unique index was verified valid.

## Verification

- Unit tests cover Vietnam midnight, scheduled lookups, historical lookup behavior, rejected requests preserving the current version, malformed configuration types, duplicate dispatch slots, scope ambiguity, precision, warehouse/region constraints, campaign activation and loyalty history.
- API tests cover bad payloads (including missing configuration value and null tier/band entries), past/today version dates, and forbidden writes by Sales.
- `SettingsValidationPostgresTests` uses a newly created localhost database, applies migrations and seed data, checks actual PostgreSQL version transitions, +07:00 normalization, pricing/ETA before and at the boundary, rejected writes, and unique-version conflict translation.
- `AtomicSettingsPhonePostgresTests` upgrades the previous schema with safe and ambiguous duplicate-phone fixtures; checks migration rollback and audit preservation; injects deferred commit failures into group writes; exercises lost-response and simultaneous campaign retries; and forces two phone writers past the pre-check to verify only one account, credential and audit commit.
- Run all PostgreSQL workflows with `FSCM_TEST_POSTGRES` pointing to a **new** localhost database named `fscm_e2e_*`. Suites refuse an existing database and never delete one.

## Scope and remaining checks

This review covers the settings and related campaign/loyalty/warehouse transitions above. Existing catalog, territory, account and retailer tests also run as regression checks; this is not a claim that every create screen in the application has been exhaustively tested.

PayOS live verification remains outside this review. Invalid-input, rollback and race tests use disposable local data. The only development-data change is the audited phone migration described above; invalid requests are not exercised on the shared development database.
