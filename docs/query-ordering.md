# Deterministic entity paging and local warnings

GraphQL entity connections now receive an explicit ascending ordering on every property of
the mapped primary key after resource scope and screen grants are applied. This includes
composite keys. It prevents EF Core's RowLimitingOperationWithoutOrderByWarning when the
paging middleware adds Skip/Take and the client supplies no sort. Explicit client sorting is
still honored; include a unique key as a tie-breaker when sorting non-unique fields.
The connection shape, authorization rules and REST/GraphQL routes are unchanged.

Bounded inventory outbox, allocation recovery, receipt sync and unassigned-account queues
also sort by their primary key (Id or PalletId) after their timestamp, so equal timestamps no longer leave selection ambiguous.
No schema/index or data migration is required.

Regression tests generate PostgreSQL SQL for every mapped GraphQL entity connection with the
row-limiting warning configured to throw. They also cover a composite key and explicit sorting.
These SQL-compilation tests open no database connection and create no database.

CS9113 means an unread primary-constructor parameter, not a failed dependency registration.
DeliveryService had unused payment-plan, payment-store and inventory-lifecycle parameters;
OrdersController had an unused delivery-service parameter. Their removal does not remove
any calls or change business workflows: those parameters had no uses. Required services
remain registered for their actual consumers.

Serilog's configured Console sink previously ran alongside an unconditional code Console
sink. The startup fallback is now used only when Console is absent from configuration, avoiding
duplicate console records without suppressing EF warnings. Existing dependency vulnerability
warnings are separate and are not hidden by this change.
