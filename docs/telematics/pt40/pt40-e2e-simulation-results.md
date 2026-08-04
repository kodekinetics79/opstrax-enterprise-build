# PT40-Q end-to-end simulation results

Device: **PT40-Q**, IMEI `862464068456321`, serial `4C4000067803`. Path: deployed REST trusted-gateway
ingest (`POST /api/telemetry/gps-ingest`), hardened by C1/C2/H2/H3.

Booted the API against the OpsTrax local integration DB, seeded a commissioned PT40-Q on tenant A + a
per-gateway credential (`khalid-gw-1`) scoped to tenant A, and a second device on tenant B. Drove real
HMAC-signed requests (HMAC-SHA256 of `"{timestamp}.{rawBody}"`):

| # | Scenario | Expected | Result |
|---|----------|----------|--------|
| S1 | Position via per-gateway HMAC auth (`X-Gateway-Id`) | 200, live map | ✅ `latest_vehicle_positions` updated, `telemetry_status=healthy` |
| S2 | Harsh-braking event (`harshEvent`, g-force 0.63/0.71) | 200 → safety event | ✅ alert `harsh_braking` → `safety_events(event_type='Harsh Braking')` |
| S3 | SOS/crash (`harshEvent=sos`) | 200 → Critical | ✅ alert `sos` → `safety_events(event_type='SOS', Critical)` |
| S4 | Cross-tenant: gateway A asserts tenant B's IMEI | 403 | ✅ H3 tenant-scope reject |
| S5 | Tampered signature | 401 | ✅ |
| S6 | Unknown `X-Gateway-Id` (no downgrade to legacy) | 401 | ✅ |
| S7 | Un-commissioned device (`CredentialRotationRequired`) | refused | ✅ field-commissioning gate |

All of the telematics completion work composes end-to-end on the deployed path: per-device auth + tenant
isolation (H3), harsh/SOS detection feeding the dashboard vocabulary (C2), non-blocking replay (C1), and
newest-wins live position (H2).

## Production commissioning notes
- Provision the forwarder credential via `POST /api/telemetry/gateways` (secret shown ONCE, stored
  envelope-encrypted; refused if the PII key isn't configured).
- Complete device credential rotation so it leaves `CredentialRotationRequired` — the platform refuses
  ingest until then. Do NOT force `status='active'` in prod; go through the rotation flow.
- Decommission the legacy shared `Telemetry:GatewaySecret` once all forwarders use per-gateway creds
  (until then that legacy path has no tenant-scope enforcement — see migration stage42).

## Deferred (docs/telematics/COMPLETION_PLAN.md)
C3 (GT06 gateway alarm/CAN-diagnostics bridge), H1 (canonical-model unification), C2-P1 (server-side
speed-delta detector + accelerometer/g-force canonical path).
