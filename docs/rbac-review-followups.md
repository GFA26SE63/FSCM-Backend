# RBAC review follow-ups

Only shipment receipt authorization is fixed in this change. The following findings are recorded for review; no changes to these flows have been applied. These are source-level findings, not reproduced end-to-end failures.

| Finding | Evidence | Review needed before changing behavior |
| --- | --- | --- |
| Goods receipt IDs are interpreted as payment receipt IDs by the common REST scope filter. | `ResourceScopeFilter.Named` maps every `receiptId` to `PaymentReceipt`, while `GoodsReceiptsController.Get` and `Approve` use that parameter for `GoodsReceipt`. | Verify assigned/unassigned warehouse access and the existing `InventoryService` checks. A missing payment receipt currently causes the filter to defer to the service; this is not evidence that normal goods receipts always return 403. |
| Scope matching depends on parameter casing and aliases. | `ResourceScopeFilter.Named` handles `BatchId` but not the `batchId` query parameter on inventory lock listing; it handles `id` but not the `lockId` route on inventory lock detail/release. | Verify authorization and scope in `InventoryLockService`/`InventoryLockStore` before changing the filter. Missing a common-filter check alone does not prove the operation is unprotected. |
| Inventory locks lack an explicit scoped entity query. | `EntityQueryService.CreateScopedQuery` has no `InventoryLock` branch, so non-global profiles fall back to an empty query. | Check the intended read contract and assigned-warehouse behavior before exposing/changing queries or routing lock checks through this query. The current `lockId` route is not matched by the common filter. |
| Payment ID collections are not covered by the common filter. | `Named` recognizes `PaymentId`/`paymentId`, but not `PaymentIds`; `RetailerReceivablesController` is also outside the filter's controller list. | Audit `RetailerDebtPayOsService` ownership checks for selected invoices, mixed-retailer inputs and checkout replay before deciding whether an additional common-filter check is necessary. This has not been reproduced as a service authorization bypass. |

Any follow-up must retain exact live API/screen grants, ownership checks, assigned warehouse scope, and normal validation/not-found responses. Do not solve receipt access by granting retailers warehouse inventory access.
