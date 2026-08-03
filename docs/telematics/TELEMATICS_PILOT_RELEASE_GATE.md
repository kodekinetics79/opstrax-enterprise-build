# Telematics customer-pilot release gate

Status date: 2026-08-02

## Honest pilot offer

The first customer pilot is an **operations overlay for an existing telematics provider**. It is not yet a replacement for Samsara, Motive, or Geotab.

The supported commercial promise is limited to capabilities that have live data paths and passing acceptance evidence:

- Samsara GPS connection and synchronization
- tenant- and branch-scoped vehicle mapping
- current position, breadcrumb history, freshness, and provenance
- geofence management and events
- device and connector exception triage
- dispatch, shipment, POD, safety, and maintenance context

Unsupported provider feeds and hardware operations must be hidden or explicitly labelled unavailable. Demo data, placeholder media, inferred protocol names, or fabricated health readings cannot be used as pilot evidence.

## GO gates

A pilot GO requires all of the following:

1. Zero open P0 or P1 security, tenant-isolation, integrity, or data-loss defects.
2. Clean-database migrations pass twice and mandatory RLS/credential grants pass independently.
3. API, telematics protocol/gateway, frontend, container, and dependency-vulnerability gates pass.
4. Device and connector failures are durable, retryable, auditable, and explainable.
5. Observed time, receipt time, normalization time, source, confidence, and unknown/stale states remain distinct.
6. Browser acceptance passes for admin, branch operator, denied user, empty, stale, degraded, and error scenarios.
7. The pilot runbook names the provider account owner, branches, assets, alert recipients, retention policy, support escalation, rollback, and success thresholds.
8. Unsupported raw-TCP hardware ingestion is excluded until a public TCP edge is deployed and independently exercised.

## Pilot scorecard

- provider authorization and initial sync success
- mapped and unmatched vehicle/device count
- device activation and online percentage
- valid-location percentage and freshness SLA
- connector success rate, lag, retry, and dead-letter count
- geofence event precision and acknowledged-alert time
- dispatch/shipment outcomes influenced by telemetry
- support incidents, time to resolution, and active user adoption

Unavailable metrics must display **unknown**, never zero or healthy.

## Missing product domains

These are roadmap gaps, not pilot-ready capabilities:

### Entirely or functionally absent

- **EV and charging intelligence:** state of charge, range, charging sessions, charger/site readiness, battery health, mixed-fleet comparison, and energy-aware routing.
- **Native video telematics and camera operations:** ingest, clip retrieval, signed playback, event timeline, live view, camera health/storage, redaction, retention/legal hold, and real detection evidence.
- **Connected equipment and trailer telematics:** powered/unpowered trackers, trailer pairing, door/cargo/reefer sensors, tamper/theft/dormancy, engine hours/PTO, and equipment utilization.
- **Production Motive and Geotab pipelines:** GPS, diagnostics, HOS/DVIR, safety, camera, asset, webhook, cursor, and reconciliation flows.
- **OEM and marketplace ecosystem:** embedded OEM feeds, partner install/consent/scopes, connector SDK/versioning/certification, upgrades, and billing.

### Required next product release

- provider onboarding, authorization, mapping, backfill, reconciliation, disconnect, and sync-health workspace
- trip playback, stops/dwell/idling, confidence, stale/no-data states, bulk filters, alert acknowledgement/escalation, and reporting
- complete DeviceOps install, activation, evidence, offline diagnosis, RMA, warranty, SIM, subscription, and bulk operations workflow
- telematics-specific precise-location/off-duty privacy and retention administration
- pilot administration, SLA/support health, diagnostic bundle, audit export, and in-product escalation
- provider-neutral capability matrix and canonical provenance/quality model
- real provider-camera incident workspace and fault-to-maintenance workflow
- shipment-aware cold-chain, detention, claims, customer ETA, and proof-to-cash automation

## Architecture constraint

The current Render web-service topology exposes HTTP, not a public raw TCP device port. A physical tracker TCP pilot therefore requires a separately authorized public TCP edge/load balancer or a supported provider/HTTP ingestion path. A private Render service alone does not make the gateway reachable by field devices.
