# Operations report demo data

Run from the FSCM-Backend repository root, using .NET 10 and access to the deployed PostgreSQL database:

```powershell
dotnet run --project tools/FSCM.ReportDemoSeed -- --month 2026-11 --apply
```

By default the connection comes from `src/FSCM.API/appsettings.json`. Use `--config <absolute-json-path>` for another backend configuration, or the `FSCM_REPORT_SEED_CONNECTION` environment variable for a connection supplied by the operator. Credentials are never printed. Check the printed database host/name against the deployment's database before using this tool.

Use `--dry-run` in place of `--apply` to insert, query all three report projections, and roll the transaction back. Apply pending backend migrations first. This tool does not migrate, clear, or reseed the database.

The monthly marker is `DEMO-RPT-yyyyMM`. Repeating the command for the same month verifies the existing data without inserting duplicates. All inserts and report verification execute in one serializable transaction, guarded by a database advisory lock. A failure rolls back the inserts.

The dataset contains:

- 3 dedicated `[DEMO]` retailers in an existing active Sales group/region.
- 1 inactive demo loyalty program, 1 inactive demo gift, and 3 demo loyalty cards.
- 6 redemption requests: Pending, Approved, Delivered, Completed, Rejected, Cancelled; saved quantities, point costs and actor timestamps.
- Opening earned points before the month, plus earned, pending, pending reversal, adjustment, expiry, redemption hold, release and completion transactions in the month. Card balances are kept consistent with the ledger.
- 3 explicitly labelled demo sales orders and accepted quotation snapshots covering campaign-only, HSD-only, and combined discounts; the order statuses are ConvertedToPo, Delivered and Completed.

These are reporting fixtures, not a complete physical delivery workflow. They do not reserve, move, or change inventory, and do not create payments. Existing products, prices, batches and the active Admin/Operator are referenced read-only. Existing retailers, programs, orders, balances and accounts are never updated. Demo rows are visible in the portal's operational lists and totals; keep them on a demo/development deployment, not a real trading database.

Refresh the portal with the matching month, clear previous program/region/status filters, and optionally filter Retailer by `DEMO-RPT-yyyyMM`. Expected report rows for a new monthly dataset: **6 redemptions, 24 in-period loyalty transactions, 3 promotion price rows**. Three additional loyalty opening entries are dated before the month.

The final `COMMITTED` line is printed only after all three report queries return nonempty data and the database transaction commits.
