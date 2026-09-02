# Telematics / IoT integration — what's real today and what a live feed needs

This note answers the standing question: *do the telematics modules (GPS Tracking,
OBD/J1939, Device Health, Cold Chain) need an external IoT service, and if so which?*

## Where the data comes from today

| Surface | Source today | Real? |
|---|---|---|
| Device Health (`/iot-devices`) | `GET /api/eld/devices` — real `eld_devices` rows (serial, model, provider, firmware, status, heartbeat), joined to real vehicles/drivers | **Real device registry** |
| GPS Tracking (`/gps-tracking`) | Same device registry; live position from `latest_vehicle_positions` / `location_events` | **Real, but sparse** |
| OBD / J1939 (`/obd-j1939`) | Authenticated diagnostic observations and active fault/hold projections | **Real evidence only; compatible registry types are not counted as readings** |
| Cold Chain telemetry | `fleet_tms_temperature_*` tables | Real rows |

The client service (`frontend/src/services/telematicsService.ts`) previously overlaid a
bundled seed fixture on top of the real device rows (spreading a random seed device's
provider / signal / power / linked-vehicle onto real units, and replaying seed telemetry
events). That overlay was removed on 2026-07-06: real API fields now win, and anything the
backend does not yet supply renders an honest `—` / "no telemetry yet" instead of a
fabricated value. Per-device telemetry **time series** (speed/RPM/coolant/fuel history,
health history) is intentionally empty until a real feed is wired — that is the gap below.

## Live ingest and provider activation

The platform HAS the ingest plumbing already:
- `POST /api/telemetry/ingest` — device-authenticated. Send `X-Device-Key`,
  `X-Timestamp`, `X-Nonce`, and `X-Signature`; compute the HMAC-SHA256 signature
  over the exact canonical string `METHOD\npath\ntimestamp\nnonce\nsha256(body)`.
- `POST /api/telemetry/gps-ingest` — trusted protocol-gateway forwarding for
  PT40/GT06-class trackers. It sends `X-Gateway-Timestamp` (Unix seconds) and
  `X-Gateway-Signature` (hex HMAC-SHA256 of `<timestamp>.<raw-json>`) using
  the **per-gateway** secret issued by `POST /api/telemetry/gateways`, identifying
  itself with `X-Gateway-Id`; IMEI is only a provisioned lookup key, never a credential.

  > **Do not set `Telemetry__GatewaySecret`.** The fleet-wide secret was removed — a
  > headerless fallback would be a cross-tenant skeleton key. Its mere presence is now
  > a `fail` in Staging/Production (`ConfigValidationService`,
  > `legacy_telemetry_gateway_secret`), and since `EnsureStartupAllowed` throws on any
  > failure, populating that variable **stops the API booting**. Gateway credentials are
  > tenant-bound encrypted rows in `telemetry_gateways`.
  Freshness/replay checks, globally unique IMEI registration, tenant-bound device/vehicle
  resolution, timestamp bounds, and ordered latest-position updates are enforced.
- SSE stream (`/api/telemetry/stream` via short-lived ticket), positions snapshot,
  live-state and alert endpoints (see `EndpointMappings.cs` ~lines 107-145)
- Tables: `location_events`, `latest_vehicle_positions`, `telemetry_live_asset_states`,
  `telemetry_alerts`, `telemetry_rules`

OpsTrax accepts native signed HTTP telemetry, tenant-bound protocol-gateway traffic, and
configured Samsara polling. Continuous customer data still requires a commissioned producer:
a real device/gateway or provider account mapped to the correct tenant assets. Until then,
the customer pages retain honest never-connected, offline, no-position, and no-diagnostic
states rather than presenting registry metadata as telemetry evidence.

## Activation options

**A. Real hardware telematics provider (true Samsara parity).** Integrate a fleet
telematics API and forward its webhooks/polls into `/api/telemetry/ingest`:
- **Samsara** — `api.samsara.com`, OAuth 2.0 / API token, rich vehicle stats
  (GPS, engine RPM, fuel, DTCs, dashcam). Best fit for the OBD/J1939 + dashcam surfaces.
- **Motive (ex-KeepTruckin)** — strong ELD/HOS + GPS; good fit for the HOS/ELD module too.
- **Geotab** — `MyGeotab` SDK, deep J1939/OBD diagnostics.
  A provider account, scoped credentials, asset mapping, provider-specific conformance
  tests, and a small real-device pilot are required before production activation.

**B. Governed certification harness (no provider account).** The staging-only harness
generates deterministic, non-personal, signed GPS and diagnostic scenarios for exactly
1,100 certification devices across five branches. It is zero-network by default, refuses
production hosts, uses the public customer/device boundary rather than SQL, verifies the
exact deployed SHA and readiness endpoint, and includes bounded replay, conflict, stale,
offline, reconnect, geofence, odometer, and critical-fault controls. This proves OpsTrax
ingest and browser behaviour; it does not prove a provider integration.

**C. Leave as-is.** Device registry, HOS, DVIR, work orders, safety, scorecards are all
real DB-backed already; only continuous GPS/engine *streams* are absent.

## PT40 field commissioning gate

Do not call a PT40 device live based on registry presence or simulated movement. Production
commissioning requires a protocol gateway/forwarder, configured APN and destination, a secret
of at least 32 random characters, and one valid signed packet observed end-to-end. A valid
signed fix updates both device heartbeat fields and advances only `Provisioning`/`Pending` to
`Active`; suspended, revoked, inactive, or unknown states fail closed. Never put the gateway
secret on the tracker, in a URL, in logs, or in source control.

## Recommendation

Use the governed harness for repeatable staging qualification, then validate the selected
provider in its test environment and finish with a small real-device pilot. Treat those as
separate evidence layers: synthetic conformance protects OpsTrax behaviour, while provider
and physical-device trials prove mapping, cadence, coverage, and field commissioning.
