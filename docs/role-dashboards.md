# Portal dashboards

`GET /api/v1/dashboard` accepts inclusive Vietnam-local `fromDate` / `toDate` (defaults: current month through today, maximum 366 days), `regionId`, `warehouseId`, and `salesGroupId`. Authentication supplies the actor and role. The database account must still be active and have the same role.

| Role | Read scope |
| --- | --- |
| Admin, Operator | All regions, warehouses and Sales groups |
| WarehouseKeeper | Directly assigned warehouses plus warehouses in explicitly assigned regions; no sales, customer or financial report data |
| SalesManager | Active groups from memberships and explicitly assigned regions, with only their Sales roster and assigned retailers; no warehouse inventory or system health data |

An inherited region label does not grant sibling warehouses/groups. Empty assignments return an empty scope, never global data. Foreign filter IDs return 403; incompatible region/child combinations return 400. Scope is resolved on every request, including after revocation. Warehouse, order, picking, QR, lock, transfer, delivery and KPI drilldowns use the same grants.

## Data meanings

- Period revenue: currently Completed orders, dated by the latest Completed history; legacy fallback is payment delivery date, then server receipt date. Amount is Payment.AdjustedAmount, falling back to order final amount. Top SKU revenue uses canonical line-net proportions and rounds each allocation to two decimals, matching existing product reports.
- Collections: confirmed receipt totals globally, or confirmed receipt allocations within filtered groups/regions. Unapplied credit belongs only to the global total. Legacy payment confirmations are included only when no confirmed receipt allocation exists, to avoid double counting.
- Current debt: outstanding Credit payments with a delivery date; overdue means due date before today. Outstanding Prepaid payments are separate.
- Current stock: physical quantity minus reservations and active locks, floored at zero; expired stock has zero availability. Near-date thresholds come from each SKU. Empty warehouses remain visible with zero stock.
- Date/group/region filters control period sales and money. Warehouse filters control stock and warehouse work only; the response explicitly states this distinction. Current inventory, debts and work queues are snapshots rather than period movement reports.
- Team includes active Sales with no completed orders. Retailer self-orders and revenue outside the active roster are shown separately, never attributed to a Sales rep. KPI comes from the existing versioned KPI report configuration.
- Queues are bounded to 30 prioritized items; expiry risks to 15, SKU ranking to 10, manager customers to 30. Counters cover the full filtered set. No profitability or inventory valuation is inferred without a cost source.

## Verification

API tests cover four Portal roles, forbidden Mobile roles, anonymous requests, forged actor/role parameters and invalid dates. A disposable PostgreSQL suite verifies two-region isolation, empty/revoked scopes, stale roles, inactive users, direct grants versus inherited labels, zero-output Sales, completion-date revenue, line revenue allocation, receipt/legacy collection deduplication, locks and expired stock. Set `FSCM_TEST_POSTGRES` to a **new** local `fscm_e2e_...` database; dashboard tests create a separate `_dash` database and refuse an existing database. Real development data was also read through a read-only PostgreSQL connection; tests do not alter business records in the shared development database.

No schema migration is required. This feature does not change payment-gateway behavior.
