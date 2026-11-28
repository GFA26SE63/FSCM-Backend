# Transport dispatch reads

The transport page uses `entities.deliveryDispatchPickingReads` for picking IDs,
order IDs, warehouse IDs and item names/quantities only. It does not hydrate scans,
inventory balances, QR lineage, pricing or receipts to render the dispatch list.
The existing `deliveryPickingReads` field remains available to older clients.

`entities.shipmentReadinessBatchRead(orderIds: [UUID!]!)` accepts at most 50 IDs.
It returns the same readiness fields as `shipmentReadinessRead`, using the same
application composer for pending warehouses, exceptions, prepaid payment status,
dispatch status and history classification. Commands continue to validate current
business state independently. No reads mutate business data.

GraphQL requires `PickingLists.List` for the compact picking list and
`Shipments.OrderReadiness` for batch readiness. Warehouse keepers see only their
assigned warehouses; order visibility follows the shared shipment order scope.
An inaccessible or unknown order rejects the batch without identifying that ID.
Empty batches return an empty array; oversized batches and empty GUIDs are invalid.

The portal deduplicates and sorts IDs, requests batches of 50 and polls those batches
every 15 seconds only while the screen is active. A failed batch marks its orders
unavailable instead of inventing readiness. Transfer history does not fetch assignment
lookup lists. Mobile keeps its existing single-order contract unchanged.

Schema/database changes and business-data seeding: none. Regression checks compare
batch results with the existing single-order service against the configured database,
verify resource scope and query budgets (2 SQL reads for picking, 4 for readiness),
and exercise the GraphQL grant contract plus frontend partial/malformed responses.
