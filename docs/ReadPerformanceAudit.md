# Read performance audit

This audit follows Portal reads and their GraphQL resolvers. It distinguishes
per-row detail hydration from bounded pagination and command validation. Command
transactions, authoritative business rules and mobile single-detail contracts are
preserved. No schema migration or business-data provisioning is required.

| Area | Confirmed issue | Change |
| --- | --- | --- |
| Gifts | REST reward detail per managed image | `LoyaltyRewardEntity.resolvedImageUrl` signs the projected stored key locally; the raw reference stays available for edits |
| Gifts | Downloading completed redemption history to calculate one displayed total | `rewardRedemptionTotals(ids)` aggregates at most 50 reward IDs in SQL, after existing redemption scope/grants; unknown legacy quantities remain null |
| Promotions | Per-product/retailer detail calls for selected targets | Native GraphQL product ID batches and `retailerProfiles(ids)` preserve exact IDs, inactive selected records, contact and category metadata |
| Promotion/shelf-life forms | Fetching all three lookup collections regardless of displayed controls | Explicit requested lookup collections and a short shared query cache |
| Loyalty programs | REST retailer page hydration for lightweight options | GraphQL selects only displayed retailer option fields |
| User assignment names | One detail call per selected ID | GraphQL ID batches with complete pagination and missing/duplicate checks |
| Region managers | One user detail request per manager; failures silently became empty scope | `userScopeBatchReads(ids)` returns authoritative scopes with at most 100 IDs; production identity store reads grant tables in 8 queries instead of per user |
| Retailer pages | Constructing MLOR/image detail for the whole catalog before paging | Construct related detail only for page IDs; selected-ID predicates are applied in the store before materialization |
| Receipt read lists | Hydrating receipt detail per header | Two SQL queries and a lookup, preserving the existing warehouse scope and batch matching |
| Refund read lists | Refund/payment/order/retailer and totals queried for every row | Two SQL queries; payment totals include every relevant obligation even under status filtering |

Already batched/scoped paths reviewed: order summary pages, promotion view assembly,
complaint list documents, disposal headers/lines, warehouse keeper/profile lookups,
and transport dispatch batches from the preceding change. Native entity GraphQL
connections and their bounded cursor loops are retained. Unused legacy Portal list
hooks are not treated as active page regressions. Per-item state-changing validation
and stock locking loops are not replaced with concurrent operations.

New GraphQL fields use the same operation grants as the reads they replace:
`RewardRedemptionTotals` -> `LoyaltyRedemptions.List`; `UserScopeBatchReads` ->
`Users.Get`, with requested users additionally filtered by the entity scope.
`retailerProfiles(ids)` retains `Retailers.List`. Resolved reward images inherit
the parent reward query's authorization. No raw upstream errors or credentials
are returned.

Regression coverage compares batched lists/scopes with existing authoritative
detail reads against the configured database, checks 2-query receipt/refund budgets
and the 8-query identity budget, verifies image-only projection and scoped GraphQL
contracts, and checks lookup batching, pagination and failure behavior in Portal.
