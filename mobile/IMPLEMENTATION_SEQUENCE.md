# OpsTrax Mobile — Implementation Sequence

## Phase M1 — Shared foundation
Status: in progress

- premium glass design system
- secure auth shell
- role-aware navigation
- loading/error/offline patterns
- mobile release gates

## Phase M2 — Driver
- Today/home
- current trip
- stop execution
- assignment acceptance
- POD/signature/photo
- incident reporting
- DVIR/compliance
- offline queue and sync state

## Phase M3 — Customer
- home
- shipments
- shipment detail/timeline
- POD/documents
- billing
- support
- notification preferences

## Phase M4 — Fleet / Dispatcher
- operations home
- authorized work queue
- live fleet/telemetry
- alerts/exceptions
- driver/vehicle state
- approval/acknowledgement actions where backend-supported

## Phase M5 — Device capabilities
- camera
- image/document picker
- location
- push notification registration
- deep links
- background behavior where justified

## Phase M6 — Security and tenancy
- cross-tenant regression
- cross-customer account regression
- driver assignment scope regression
- revoked/expired token behavior
- upload authorization
- IDOR/BOLA test pack

## Phase M7 — Mobile quality
- iOS rendering
- Android rendering
- accessibility
- daylight readability
- low-network testing
- large-list performance
- crash/error handling

## Phase M8 — Distribution
- internal iOS build
- internal Android build
- TestFlight/internal testing
- Google Play closed testing
- reviewer/test tenant preparation

## Phase M9 — Field pilot
- real driver
- real fleet manager/dispatcher
- real customer
- production-like tenant data
- critical issue remediation

## Phase M10 — Public release
Only after `READY_FOR_PUBLIC_STORE_SUBMISSION` verdict.
