# Retailer requests: complaints, returns and damaged-goods collection

The existing `/complaints` routes now serve the unified **Yêu cầu** area. Existing complaint and image-evidence clients remain compatible.

## Workflow

1. Retailer selects the received order/SKU/batch/shipment and requests Complaint, Return or DamagedCollection. They specify affected cartons, reason and preferred DebtOffset/CashRefund. None is permitted only for collection without compensation.
2. Admin approves a whole-carton quantity and confirms the settlement method, or rejects with a reason. Operator can request evidence but cannot approve/reject through either the old resolve endpoint or the new endpoints.
3. Physical cases create ReturnOrder (goods/financial return) and CollectionOrder (company pickup) as applicable. These are handling documents within the original request; they do not create another sales order or sellable inventory.
4. Admin/Operator records the external handling time, actual quantity, note and evidence/receipt reference. Actual quantity may be zero and cannot exceed approval. Admin may waive physical handling with a mandatory reason.
5. Once handling is complete and the whole order has activated its receivable, the transaction reduces the original order obligation and creates a PendingRefund document for only the newly created paid surplus. Admin uses **Chốt tài chính** if handling finished before full delivery. Collection-only cases close without money movement.
6. Existing manual refund evidence/independent Admin verification completes the actual payment. Creating a refund document never means the transfer has been verified.

Example: original obligation 1,000,000 VND, confirmed collections 600,000, accepted return value 500,000 => debt offset 400,000 + refund document 100,000. Preference cannot divert unpaid debt into cash. Actual settlement uses ledger values at finalization, including payments made after approval.

## Integrity and permissions

- Received shipment/allocation and its final net batch price are authoritative; unrelated QR labels are rejected.
- Independent cumulative financial and physical quantity budgets include pending requests. Rejected/cancelled cases release their budgets. Actual/approved settlement overrides are included in financial budgeting.
- RequestId with content hash prevents replay duplication; version checks protect approval/handling. Repeated handling with identical timestamp/quantity/evidence replays safely. Changed retries conflict.
- Physical confirmation, receivable adjustment, refund creation and final request state commit atomically. Goods handling can be saved while delivery is incomplete, with finance visibly pending.
- Damaged goods are never automatically returned to saleable stock.
- Financial approval is Admin only; external handling is Admin/Operator; Retailer sees and changes only their own undecided requests/evidence.

## Migration and rollout

Deploy Backend migration `20261111171909_AddReturnCollectionRequests` before updated Portal/Mobile.

The migration adds request fields and handling documents, backfills legacy affected quantities and checks actual payment operations/audit evidence before marking an adjustment posted. Posted legacy cases close without touching monetary ledgers. Legacy decisions with no posting evidence stay Approved, physical-complete and await Admin financial reconciliation. The migration never creates refunds or repeats adjustments. Historical splits that were not recorded remain explicitly labelled as legacy in both clients; existing refund obligations stay available in the refund module.

Validation includes unit tests of mixed debt/refund splits, API role checks and real PostgreSQL end-to-end cases: partial receipt handling before complete delivery, approval without early finance, partial physical confirmation, concurrent replay with one refund only, collection without reimbursement, and legacy migration rehearsal with unchanged payment/refund amounts.

Development database upgrade verified on 2026-11-12: all three legacy financial cases migrated; two with posting evidence are Resolved, one without posting evidence remains Approved/awaiting reconciliation. No new handling documents or refund records are created by this migration.
