# OpsTrax Mobile — Store Release Gates

Public store submission is blocked until the gates below pass.

## G0 — Product truth
- Driver, Fleet, and Customer roles use real backend data.
- No placeholder operational records remain in release builds.
- No UI claims unsupported by backend capability.
- No ELD/HOS certification claim until separately certified.

## G1 — Authentication and tenant boundary
- Login resolves tenant server-side.
- Secure token storage is used.
- Expired/revoked sessions fail closed.
- Tenant A cannot access Tenant B.
- Customer A cannot access Customer B within the same tenant.
- Driver cannot access another driver’s assignment unless explicitly authorized.
- IDOR/BOLA tests pass.

## G2 — Driver field execution
- Assignment acceptance works against real API.
- Trip/stop status changes are authorized and persisted.
- POD capture works.
- Signature/photo/document capture works where enabled.
- DVIR/inspection flow works.
- Incident flow works.
- Offline behavior is explicitly tested.
- Duplicate submission is prevented through idempotency where required.

## G3 — Customer experience
- Active shipment visibility works.
- ETA/status uses real backend data.
- POD/document visibility respects account scope.
- Billing visibility respects finance permissions.
- No internal fleet data leaks through customer APIs.

## G4 — Fleet/dispatcher experience
- Operational work list uses authorized backend records.
- Fleet state/telemetry uses production-shaped data.
- Exceptions and status are truthful.
- Mobile actions are permission-checked server-side.

## G5 — Mobile security
- Secure storage reviewed.
- Sensitive values absent from logs.
- No hard-coded tokens, tenant IDs, secrets, or test accounts.
- Network security configuration reviewed.
- Deep links validated.
- File uploads validated server-side.
- Dependency/security scan passes.

## G6 — Privacy and permissions
- Camera permission requested contextually.
- Location permission requested contextually.
- Background location used only when required and disclosed.
- Notification permission education is clear.
- Privacy policy and data disclosures match actual behavior.
- In-app account deletion path exists when account creation requirements apply.

## G7 — UX/accessibility
- Glass UI remains readable in bright ambient light.
- Touch targets are appropriate for driver use.
- Loading/empty/error/offline states exist.
- Dynamic text/font scaling checked where practical.
- Interactive controls expose accessibility labels/states.
- Critical status is never color-only.

## G8 — Performance/reliability
- App startup checked on representative iOS/Android devices.
- Large shipment/fleet lists remain usable.
- Image/document upload failure paths tested.
- Background/foreground transitions tested.
- Network drop/reconnect behavior tested.
- Crash/error monitoring configured before public release.

## G9 — Internal distribution
- iOS internal/TestFlight build installs and launches.
- Android internal/closed-test build installs and launches.
- Production-like tenant test accounts verified.
- Store metadata and reviewer instructions prepared.

## G10 — Field pilot
- Driver field pilot completed on real phones.
- Customer pilot completed.
- Fleet/dispatcher pilot completed.
- Critical defects closed.
- Security and tenant-isolation regression passes after final fixes.

## Final verdicts

- `NOT_READY_WITH_BLOCKERS`
- `READY_FOR_INTERNAL_TESTING`
- `READY_FOR_CLOSED_PILOT`
- `READY_FOR_PUBLIC_STORE_SUBMISSION`

Only the final verdict permits public App Store / Google Play submission.
