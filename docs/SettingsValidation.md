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

Invalid publish requests are rejected before changing the previous version. PostgreSQL unique-version/open-version constraints remain the final guard for concurrent publishers; a uniqueness collision is returned as a conflict requiring reload. This change does not require a schema migration.

## Verification

- Unit tests cover Vietnam midnight, scheduled lookups, historical lookup behavior, rejected requests preserving the current version, malformed configuration types, duplicate dispatch slots, scope ambiguity, precision, warehouse/region constraints, campaign activation and loyalty history.
- API tests cover bad payloads (including missing configuration value and null tier/band entries), past/today version dates, and forbidden writes by Sales.
- `SettingsValidationPostgresTests` uses a newly created localhost database, applies migrations and seed data, checks actual PostgreSQL version transitions, +07:00 normalization, pricing/ETA before and at the boundary, rejected writes, and unique-version conflict translation.
- Run all PostgreSQL workflows with `FSCM_TEST_POSTGRES` pointing to a **new** localhost database named `fscm_e2e_*`. Suites refuse an existing database and never delete one.

## Scope and remaining checks

This review covers the settings and related campaign/loyalty/warehouse transitions above. Existing catalog, territory, account and retailer tests also run as regression checks; this is not a claim that every create screen in the application has been exhaustively tested.

The Portal saves the four immediate allocation/order settings through separate requests, and creates per-SKU campaign records through separate requests. A network failure partway through can leave a partially saved group; atomic bulk-save behavior is not introduced here. Concurrent account creation with the same phone number is also outside this patch: the account table currently has no unique phone index. These remain separate follow-up items, not verified guarantees of this PR.

PayOS live verification remains outside this review. PostgreSQL tests run on disposable local data; production data is not modified to exercise invalid requests.
