Retailers with an approved 7/14/30-day credit term can switch an unpaid prepaid order to credit when PayOS checkout is unavailable. Ownership, version and payment state are checked, and the payment update, PO and picking creation share one transaction. Existing PayOS transactions must be confirmed cancelled before switching.

Depends on #104. Validation: 350 unit tests and 44 integration tests passed. Shared demo PostgreSQL: DH-001058 switched to 14-day credit with zero money collected; retry produced exactly one PO and one change audit. DH-001057 remains prepaid for the retailer to choose.
