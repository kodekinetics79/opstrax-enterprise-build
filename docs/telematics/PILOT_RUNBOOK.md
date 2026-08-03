# Telematics Customer Pilot Runbook

## Supported pilot SKU

The pilot is an OpsTrax operations overlay using the Samsara GPS connector and the authenticated native HTTP ingest. It includes device inventory, live/latest location with provenance and freshness, breadcrumbs, geofences, alerts, device exceptions, and OBD/J1939 fault/diagnostic-hold workflows.

Raw GT06 TCP, Motive/Geotab pipelines, cameras/video, OTA commands, installer workflows, EV/charging, and connected-equipment telemetry are not enabled or represented as supported.

## Named pilot configuration

Record these values in the customer pilot ticket before activation:

- Customer executive owner and OpsTrax pilot owner
- Provider/connector owner and escalation contact
- Included company, branches, vehicles, devices, drivers, and user roles
- Alert recipients, on-call schedule, and severity/escalation rules
- Precise-location retention and off-duty/privacy decision
- Pilot start/end dates, support hours, and rollback decision owner

Never place provider tokens, device API keys, HMAC secrets, or encryption keys in the ticket.

## Entry gates

- Stage66 and Stage67 migrations and the terminal Stage58/59/67 reconciliation pass on a clean database.
- Backend, frontend, and Telematics solution tests pass; container and dependency scans are green.
- Provider scope test, asset mapping, initial backfill, and sync-cursor health are verified.
- Every included device has a branch, vehicle mapping, activation evidence, and a valid observed timestamp.
- Pilot users pass login, branch-isolation, live-map, stale/no-fix, breadcrumb, geofence, alert, fault, hold acknowledge, and hold resolve UAT.
- Browser UAT evidence is attached to the pilot ticket.

## Success scorecard

Agree numeric targets before launch. Recommended defaults:

- Activation rate: at least 95% of included devices
- Online/valid-location rate: at least 95% during operating hours
- Current location: at least 95% within the agreed freshness SLA
- Connector success: at least 99% scheduled runs; no unresolved cursor stall
- Unmatched assets: zero before operational use
- Critical alert acknowledgement: within 15 minutes during support hours
- Geofence validation: at least 98% against the agreed test routes
- No cross-tenant/branch disclosure, lost accepted event, or secret exposure

## Daily operating procedure

1. Review the Telematics Control Tower evidence scorecard and exception queues.
2. Reconcile never-connected, stale/no-fix, unmatched, failed-sync, and active diagnostic-hold items.
3. Confirm provider cursor/last-success time and investigate any 15-minute retry cooldown.
4. Acknowledge alerts and diagnostic holds; resolution requires an authorized user plus a technician scan, provider diagnostic, or service-record reference. Resolution closes the linked fault before vehicle release is evaluated.
5. Export the daily scorecard and record support incidents, customer feedback, and corrective actions.

## Stop and rollback

Stop operational reliance immediately for cross-tenant data exposure, accepted-event loss, credential exposure, materially false location time, repeated provider cursor stall, or unresolved critical safety hold behavior.

Disable the affected connector/device credentials, preserve audit and rejection ledgers, return dispatchers to the customer's incumbent provider, and keep OpsTrax data read-only for investigation. Database rollback is forward-fix only: do not reverse Stage58/59/66/67 or restore plaintext credentials. The pilot owner documents cause, affected interval/assets, customer notification, remediation, and re-entry evidence.
