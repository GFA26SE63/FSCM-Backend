# Dynamic background job settings

Background jobs read active, versioned SYSTEM_CONFIGURATION rows. appsettings no longer
contains BackgroundJobs. Apply migration 20261128100000_MoveBackgroundJobsToDatabase before
starting this version. It adds required system reference settings only and preserves existing
active rows. No schema change or business/demo provisioning is needed; the EF snapshot and
existing constraints remain unchanged.

| Key | JSON fields and bounds |
| --- | --- |
| jobs.date_tracker | enabled; timeZone (system-supported zone); runAtLocalTime (HH:mm) |
| jobs.inventory_outbox | enabled; pollIntervalSeconds (1–300); batchSize (1–500) |
| jobs.order_deadline | enabled; pollIntervalMinutes (1–60); prepaidPaymentWindowDays (1–30) |
| jobs.allocation_recovery | enabled; existing fixed 1-minute recovery interval |

Read with GraphQL businessConfigurations, under the existing BusinessConfigurations.List
operation policy. Full results require Admin or the config screen grant; other callers receive
only the same order-step/demo subset as REST. The /config page also reads currentTransitTable,
transitTableHistory and deliverySchedule through GraphQL with their existing operation policies.
Mutations remain PUT /api/v1/configurations/batch or PUT /api/v1/configurations/{key},
using existing update authorization, atomic persistence and unique version constraints.
JSON uses camelCase field names.

Workers refresh settings at most 15 seconds apart even while disabled. Changes affect subsequent
sweeps; an in-flight sweep finishes. The date tracker catches up once per local day after the
configured time, including after restart. The expiry sweep is idempotent. Business expiry dates
remain in Vietnam time regardless of the scheduler zone. Failed reads do not run jobs using
file fallbacks; the worker logs and retries. Each API instance runs its own workers, as before.

The prepaid payment window is evaluated from quotation AcceptedAt on every sweep, including
existing pending orders. Reducing it may cancel those orders sooner. It is separate from
payment.payos_link_lifetime_minutes: renewing a checkout does not renew the order window.
The initial values match the previous shipped configuration (01:30 Vietnam, outbox 15 seconds /
50 messages, deadline 5 minutes / 3 days, recovery enabled). Environments with custom legacy
values must publish their desired values through /config.

The UI has Business, Transit, Delivery schedule and Background jobs tabs. Switching tabs keeps
unsaved values mounted. JWT, provider credentials and database connections remain outside this
change. No secrets are exposed by the added settings.
