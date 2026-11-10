Provide a realistic multi-batch demo and the official price snapshots needed to inspect its results. Receive nine product/batch combinations at each of Thủ Đức, Quận 7 and Củ Chi using valid shelf-life dates, real receipt approvals and date-based idempotency keys.

Order responses now expose official totals and pricing time after allocation. Quantity-promotion amounts come from their own snapshots, so HSD reductions no longer appear as campaign discounts or trigger a false Web reconciliation error.

Shared demo PostgreSQL validation: 27 receipt lines (1,470 cartons) produced 26 new batches and replenished one existing batch. Re-running the script added no stock. DH-001059 split SKU002 across three batches and SKU005 across two, with persisted distance/score snapshots. Official total 93,050,000 VND equals the batch sum; indicative total is 96,600,000 VND. Invalid future-production legacy free stock was quarantined with inventory locks, preserving reservations. KM-005 remains paused after its create/activate/deactivate check.

Validation: 351 unit tests and 44 integration tests passed; API build passed; real DB/API and browser checks passed. Stacked on #105 to retain the current checkout fixes. No migration, credentials or runtime configuration included.
