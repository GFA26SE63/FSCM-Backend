# Migration and provisioning audit

Scope: all 94 migration implementation files (designer files and the model snapshot are metadata). Applied migration history remains unchanged.

Business/demo provisioning is performed manually using reviewed SQL in DBeaver against the single database configured in appsettings. No seed/reset was executed during this change. No schema change or new migration is needed.

## Runtime entry points removed

- API development seeder, Admin bootstrapper, and their DI registrations/configuration.
- Default-role repair seeding from the role persistence contract.
- Standalone report demo-data CLI.
- Old API seed/bootstrap switches now fail before host creation or database access.

Workflow fixtures live only in the integration-test assembly and are explicitly registered by isolated tests. Allocation benchmark RNG seeds generate JSON fixtures and do not provision database data.

## Migration inventory

Data SQL includes historical backfills, normalization, archival, system reference grants/mapping, and trigger/function bodies. A SQL occurrence does not imply a demo-data seeder. These operations are preserved as requested. The inventory flags statements in both Up and Down; it is not a proposal to execute rollback.

| Migration | Data SQL / function body present |
| --- | --- |
| 20261012160828_InitialPhysicalSchema | No data SQL detected |
| 20261012161929_AddIdentityAuthenticationSchema | No data SQL detected |
| 20261012164307_AddCatalogProductLifecycle | No data SQL detected |
| 20261013034822_AddWarehouseConfiguration | Yes — retained |
| 20261013040332_AddTerritorySalesGroups | Yes — retained |
| 20261013054509_AddRetailerManagement | Yes — retained |
| 20261013060101_AddReceiptBatchInventory | No data SQL detected |
| 20261013061641_AddQrLabelLineage | Yes — retained |
| 20261013063210_AddInternalTransfers | No data SQL detected |
| 20261013142307_AddPromotionBatchDiscount | No data SQL detected |
| 20261014053315_EnforceInventoryLifecycleIdempotency | No data SQL detected |
| 20261014060420_AddAssistedOrderWorkflow | Yes — retained |
| 20261015003504_AddPickingWorkflow | No data SQL detected |
| 20261015033845_AddOrderDeliveryReceipt | Yes — retained |
| 20261015054312_AddFinanceReceivablesPayment | Yes — retained |
| 20261015055703_IntegratePayOsPayments | No data SQL detected |
| 20261020101920_AddManualPayment | No data SQL detected |
| 20261020154727_AddIdentityProviderAndRegionScope | Yes — retained |
| 20261020162634_AddS1BCatalogTerritoryMaps | Yes — retained |
| 20261021054315_AddS1DReceiptQrInventoryExpiry | Yes — retained |
| 20261021115541_AddS1EPaymentFoundation | Yes — retained |
| 20261021142250_AddS1COrderCaptureReads | No data SQL detected |
| 20261021152147_AddSharedTransitTable | No data SQL detected |
| 20261022000651_CompleteS1BWarehouseRetailerMlor | Yes — retained |
| 20261022063140_CompleteS1COrderPricingSnapshots | Yes — retained |
| 20261022065759_CompleteS1DGoodsReceiptApproval | Yes — retained |
| 20261022070207_AddAllocationRun | No data SQL detected |
| 20261022072159_AddManualRefundWorkflow | Yes — retained |
| 20261022094647_AddShelfLifeDiscountTable | No data SQL detected |
| 20261022103311_AddAllocationRunPlan | No data SQL detected |
| 20261023022326_AddAllocationPlanVersion | No data SQL detected |
| 20261023023831_AddRunAllocationReservation | No data SQL detected |
| 20261023030221_AddRunPickingLists | No data SQL detected |
| 20261023034045_AddAllocationPriceComponents | No data SQL detected |
| 20261023052852_DropAllocationBatchSwapColumn | No data SQL detected |
| 20261023070456_AddAllocationTrace | No data SQL detected |
| 20261023095716_AddInventoryLock | No data SQL detected |
| 20261023100544_AddCategoryCodeSequence | No data SQL detected |
| 20261023101457_CompleteRunPickingWorkflow | No data SQL detected |
| 20261023101536_AddBatchQrLabelGoodsReceipt | No data SQL detected |
| 20261023102347_AddDriverCodeSequence | No data SQL detected |
| 20261023105023_AddShipment | No data SQL detected |
| 20261023105857_AddShipmentAssignment | No data SQL detected |
| 20261023125833_RepointShipmentToRunPickingList | No data SQL detected |
| 20261023164815_AddShipmentReceiptActuals | No data SQL detected |
| 20261024034359_AddLoyaltyProgramScopeAndExpiry | No data SQL detected |
| 20261024163321_AddLoyaltyRewardImage | No data SQL detected |
| 20261024163819_AddRewardRedemptionIdempotency | No data SQL detected |
| 20261024164045_AddRetailerLoyaltyHeldPoints | No data SQL detected |
| 20261026091339_AddNotificationClientContract | No data SQL detected |
| 20261026132550_SeparateBatchesByProductionDate | Yes — retained |
| 20261027053245_AddComplaintShipmentPriceLink | No data SQL detected |
| 20261027091804_AddSalesOrderQuoteAndPurchaseOrder | No data SQL detected |
| 20261030091942_ReplaceEntraWithGoogle | Yes — retained |
| 20261030152314_AddUserAccountPhone | No data SQL detected |
| 20261031052853_AddRetailerActivationOtp | No data SQL detected |
| 20261031130150_NormalizeReview2PaymentReceiptsAndWarehouseRegions | Yes — retained |
| 20261031130704_ConvertLegacyCodSeedValues | Yes — retained |
| 20261031131316_RestrictPaymentReceiptAllocationDeletion | No data SQL detected |
| 20261031132511_NormalizeReview2EnumsAndArchiveLegacyTables | Yes — retained |
| 20261031133824_LinkPickingListsToPurchaseOrders | Yes — retained |
| 20261031173002_DropReview2LegacyHistoryTables | No data SQL detected |
| 20261101141131_NormalizeRetailerContactAccount | Yes — retained |
| 20261101144547_AddRetailerDeclarationContacts | No data SQL detected |
| 20261102144705_AlignReview2OrderPaymentPickingAndComplaints | No data SQL detected |
| 20261103090000_AddCancelledPaymentStatus | No data SQL detected |
| 20261103110236_AddBlobImageStorage | No data SQL detected |
| 20261104075026_AddAllocationPriceSnapshot | No data SQL detected |
| 20261104103542_AddManualRefundClaimLink | No data SQL detected |
| 20261110164750_FreezeLoyaltyEarningRate | No data SQL detected |
| 20261110171226_AddRetailerDebtPayOsCheckout | No data SQL detected |
| 20261111052230_AddClaimCancelledStatus | No data SQL detected |
| 20261111120430_AddUniqueAccountPhone | Yes — retained |
| 20261111171909_AddReturnCollectionRequests | Yes — retained |
| 20261112043459_AddRedemptionReportingSnapshots | No data SQL detected |
| 20261112153513_AddWarehouseDocumentsAndCartons | No data SQL detected |
| 20261113012142_AddFactoryTraceability | No data SQL detected |
| 20261113012821_ExpandFactoryStockCodes | No data SQL detected |
| 20261113051841_AddRetailerCreditLimit | No data SQL detected |
| 20261113064336_AddFactoryReceiptSync | No data SQL detected |
| 20261114033559_AddCategoryDefaultMlor | No data SQL detected |
| 20261114170000_SeparateWarehousePicking | Yes — retained |
| 20261114190000_AddReceiptInspection | No data SQL detected |
| 20261122043706_AddRoleAndPermissionManagement | No data SQL detected |
| 20261123061450_AddShipmentAssignmentStatusIndexes | No data SQL detected |
| 20261123104338_ReintroduceWarehouseTransferWorkflow | No data SQL detected |
| 20261124020000_GrantFactoryRequestsScreen | Yes — retained |
| 20261126143711_LinkWarehouseIssueToTransportDispatch | No data SQL detected |
| 20261126193313_LinkAccountsToDynamicRoles | Yes — retained |
| 20261126193606_EnforceDynamicRoleLinks | No data SQL detected |
| 20261126200509_AddScreenApiMapping | Yes — retained |
| 20261126200924_ConstrainMobileScreenApiMapping | Yes — retained |
| 20261113064338_InitialFactoryDatabase | No data SQL detected |
| 20261114190000_AddReceiptInspection | No data SQL detected |

## SQL provisioning requirements

Apply pending migrations first. Review scripts in a transaction, respect foreign keys and unique constraints, and provision account role-definition links and business scopes consistently. Local identity credentials must use the application-compatible password hash format; do not store plaintext passwords.

For RBAC, SYS_ROLE.BaseRole defines business scope and USER_ACCOUNT.RoleDefinitionId links the effective role. Screen/API grants in SYS_ROLE_PERMISSION must agree with SYS_SCREEN_API_MAPPING: API grants are the distinct union mapped from enabled screens for the base role. Prefer the role-management command for later permission changes because it enforces this synchronization transactionally. SQL changes must preserve the same invariant.

There is no permission cache to clear: authentication/authorization re-read current persisted role permissions on subsequent requests. Never delete migration history or replay old migrations to reseed business data.
