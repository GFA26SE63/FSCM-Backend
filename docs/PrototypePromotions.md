# Prototype promotion rules

The initial FSCM_Prototype.html defines two campaign types. Type 1 has a threshold and percentage for every selected SKU. Type 2 totals quantities of the selected SKU group within one order and applies its shared percentage to matching lines. Each line takes its highest eligible percentage, never the sum of competing campaigns. Shelf-life discounts remain a separate allocation step.

The previous form forced all conditions into a single shared threshold/rate. It has been replaced with separate PerSku and ProductGroup setup, code/name product search, scoped retailer selection, Vietnam date boundaries, and explicit save-draft or save-and-activate actions. Existing campaigns migrate to ProductGroup, preserving their existing behavior. Programs already used by an order cannot have their conditions edited; pause/resume remains available.

Migration AddPrototypePromotionRules adds only CampaignMode to PROMOTION and nullable MinimumQuantity/DiscountPercent to PROMOTION_PRODUCT. It does not reprice orders or modify existing campaign conditions. Mobile snapshots include the new rules so both online pricing and cached estimates use the correct type.

## Shared demo verification on 7 November 2026

DH-001060 was priced at 18:54, before KM-001 and KM-005 were activated at 18:56. Its zero campaign discount was therefore retained as a locked order price. KM-004 targets two other retailers, not DEMO-TEST-1105. Its configured 90% is a ninety-percent reduction; the name “Mua 10 giảm 1” does not convert the numeric field into a ten-percent discount.

Two new campaigns are active only for DEMO-TEST-1105:

- KM-006: SKU001 from 10 cartons gets 11%; SKU002 from 30 cartons gets 9%.
- KM-007: the combined SKU001/SKU002 group from 40 cartons gets 12%.

Actual API checks before enabling KM-007 returned 11% and 9% independently. With KM-007 enabled, 9+30 cartons returned 0% and 9%; 10+30 returned 12% on both lines. Thus the group threshold does not leak into independent SKU rules and the best campaign wins without stacking.

DH-001061 was created as a new Credit order, 10 SKU001 plus 30 SKU002. It reached Quoted, with gross 12,120,000 VND, campaign discount 1,454,400 VND, final 10,665,600 VND. It is left unconfirmed for review. An additional 120 cartons of SKU002 were received/approved using an idempotent demo receipt to support the test and leave stock for retrying in the app.

DH-001060's amount and version were checked unchanged. Editing the used KM-007 threshold was rejected with HTTP 409. The migration was applied twice, and the second run made no changes. Retailer mobile/snapshot returned two independent KM-006 rules and ProductGroup KM-007.
