Orders currently expose only pooled availability, hiding why particular warehouses cannot serve the retailer. Return per-warehouse eligible quantity, route distance and unavailability reason using the same delivery, expiry, reservation and lock filters as allocation. Classify outside-service warehouses even when their stock is zero or MLOR is missing, so Mobile can consistently hide them. Reuse the allocation engine's configured service-radius comparison. Preserve the aggregate field for quantity validation and older clients; actual automatic warehouse/batch allocation is unchanged.

Validation: 358 unit tests and 44 integration tests passed. Checked the updated runtime against shared demo PostgreSQL: SKU001 exposes WH-Q7 10, SKU002 exposes WH-TD 220 / WH-Q7 70, and WH-HCM-01 remains zero with OutsideServiceRadius. Availability is read-only and requires no migration.

Stacked on #107 to preserve the earlier order eligibility and prototype promotion fixes.
