# Background job persistence

Automated order completion writes `null` for `changedByUserId` in order history.
Human-triggered transitions preserve the actual user GUID and its foreign key.
REST order detail and GraphQL `orderStatusHistories` expose the nullable GUID.
The frontend history contract accepts `string | null`; mobile does not consume this field.
No synthetic system account or business-data seeding is required.

Expiry sweeps check for an existing `BatchExpired` event by aggregate and event type,
including processed and failed messages, before emitting another event. A stale batch
status is still corrected and any remaining unlocked stock is locked. Batch version
concurrency and atomic SaveChanges prevent competing replicas from committing duplicate
stock effects. A losing concurrent sweep records failure and retries on its next run.

When a sweep fails, the store clears failed tracked business changes and saves only
the failed run. Order completion uses a separate scope per order so one failure does
not prevent the other orders from being evaluated.

Regression checks include stale status with an existing expiry event, failure reporting,
and rollback-only checks against the database configured in ignored API appsettings.
