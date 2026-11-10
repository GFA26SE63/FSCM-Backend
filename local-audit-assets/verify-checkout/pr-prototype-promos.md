Restore the two promotion types in the original FSCM_Prototype.html: independent minimum/rate rules per SKU, and shared minimum/rate based on the total matching SKU quantities in one order. Each line receives the best eligible campaign without stacking. Add explicit activate-on-save while keeping legacy creation as draft, and reject edits to conditions once a campaign has been applied to an order.

AddPrototypePromotionRules adds three columns, preserving existing campaigns as ProductGroup and all captured order prices. Mobile snapshots carry mode and per-SKU rules. Stacked on #106.

Validation: 356 unit and 44 integration tests passed. Migration applied and rerun on the shared demo PostgreSQL database. Actual preview: independent SKU rules returned 11%/9%; group total 39 returned 0%/9%, total 40 returned 12%/12%. New DH-001061 reduced 12,120,000 VND by 1,454,400 VND to 10,665,600 VND. Editing applied KM-007 returns 409. Existing confirmed DH-001060 amount/version are unchanged. Retailer mobile snapshot and live Web UI checks passed.

The reported old order was priced at 18:54 before relevant campaigns were activated at 18:56; KM-004 also targets other retailers. Old orders are intentionally not repriced.
