# Multi-batch allocation demo

The shared demo database was populated on 7 November 2026 through the goods-receipt create/approve API, not by overwriting balances or existing orders.

## Recreate inventory

Run `New-AllocationDemoInventory.ps1` with the API v1 URL and an authenticated Admin header. Do not save access tokens or passwords in the repository. The business date defaults to the Vietnam calendar date. Repeating the same business date uses the same receipt idempotency key and does not receive stock twice. Use a new date only when a new demo receipt is intended.

Each of WH-TD, WH-Q7 and WH-HCM-01 receives these nine lines. Production date is expiry minus the product's shelf-life days, so none is manufactured in the future.

| SKU | Shelf life | Days to expiry | Quantities per batch |
| --- | --- | --- | --- |
| SKU001 | 7 days | 1, 3, 6 | 20, 40, 80 |
| SKU002 | 14 days | 2, 7, 12 | 20, 50, 90 |
| SKU005 | 30 days | 4, 14, 26 | 30, 60, 100 |

This creates 27 receipt lines (1,470 cartons). On the current database, one line shares the existing WH-TD/SKU001 production and expiry dates, so it adds to that batch: 26 new batch records, not 27. Receipts are PN-261107-01, PN-261107-02 and PN-261107-03.

Free inventory on five legacy batches with future production dates was held using `QualityHold` inventory locks. Existing reservations, quotes and payments were preserved. These holds have notes beginning `DEMO cleanup: legacy batch has future production date` and can be inspected in inventory-lock history.

## Verified order

Retailer DEMO-TEST-1105 placed DH-001059 with SKU002 × 180 and SKU005 × 120. It is left at Quoted so the team can inspect the allocation and accept the quote manually. The order reserves 300 cartons until its normal quote expiry or cancellation.

| SKU | Sequence | Batch | Cartons | Distance | Score |
| --- | --- | --- | --- | --- | --- |
| SKU002 | 1 | WH-TD-SKU002-31102026-14112026 | 50 | 6.016 km | 0.650318 |
| SKU002 | 2 | WH-Q7-SKU002-31102026-14112026 | 50 | 8.353 km | 0.636640 |
| SKU002 | 3 | WH-TD-SKU002-02112026-16112026 | 80 | 6.016 km | 0.699525 |
| SKU005 | 1 | WH-TD-SKU005-22102026-21112026 | 60 | 6.016 km | 0.689426 |
| SKU005 | 2 | WH-Q7-SKU005-22102026-21112026 | 60 | 8.353 km | 0.754380 |

The algorithm applies route/radius and remaining-life-at-arrival filters before scoring. WH-HCM-01 is approximately 33.4 km from this retailer, beyond the configured 20 km limit. Short-lived local batches fail MLOR; they must not become orderable just because their physical stock is positive. The eligible batches then compete by remaining life, distance and demand coverage. Coverage is recalculated after each take, so scores at different sequence numbers are not a single static ranking. This is a weighted greedy heuristic, not proof of a globally optimal plan.

Inspect `allocationFragments` in order detail for persisted scoring/distance snapshots. The legacy `allocations` projection may contain zero diagnostic fields and should not be used as the explanation source. The Web order allocation dialog uses fragments.

## Promotion visibility check

KM-004 (the team's saved campaign) still exists and is paused. Creation deliberately stores a campaign inactive until activation. The management list must request `includeInactive=true`, otherwise newly saved and paused campaigns disappear. KM-005 is a separate, paused demo campaign used to verify create → activate → deactivate → reload against PostgreSQL. It remains inactive and does not affect pricing.
