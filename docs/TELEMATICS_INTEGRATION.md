# Telematics / IoT integration — what's real today and what a live feed needs

This note answers the standing question: *do the telematics modules (GPS Tracking,
OBD/J1939, Device Health, Cold Chain) need an external IoT service, and if so which?*

## Where the data comes from today

| Surface | Source today | Real? |
|---|---|---|
| Device Health (`/iot-devices`) | `GET /api/eld/devices` — real `eld_devices` rows (serial, model, provider, firmware, status, heartbeat), joined to real vehicles/drivers | **Real device registry** |
| GPS Tracking (`/gps-tracking`) | Same device registry; live position from `latest_vehicle_positions` / `location_events` | **Real, but sparse** |
| OBD / J1939 (`/obd-j1939`) | Device registry filtered to ELD/OBD/gateway types | Real devices; **per-reading diagnostics are not yet fed** |
| Cold Chain telemetry | `fleet_tms_temperature_*` tables | Real rows |

The client service (`frontend/src/services/telematicsService.ts`) previously overlaid a
bundled seed fixture on top of the real device rows (spreading a random seed device's
provider / signal / power / linked-vehicle onto real units, and replaying seed telemetry
events). That overlay was removed on 2026-07-06: real API fields now win, and anything the
backend does not yet supply renders an honest `—` / "no telemetry yet" instead of a
fabricated value. Per-device telemetry **time series** (speed/RPM/coolant/fuel history,
health history) is intentionally empty until a real feed is wired — that is the gap below.

## The gap: there is no live ingest feed

The platform HAS the ingest plumbing already:
- `POST /api/telemetry/ingest` — device-authenticated (HMAC-SHA256 over
  `X-Device-Key + X-Timestamp + X-Nonce + X-Signature`)
- SSE stream (`/api/telemetry/stream` via short-lived ticket), positions snapshot,
  live-state and alert endpoints (see `EndpointMappings.cs` ~lines 107-145)
- Tables: `location_events`, `latest_vehicle_positions`, `telemetry_live_asset_states`,
  `telemetry_alerts`, `telemetry_rules`

What's missing is a **producer**: something actually POSTing device data into that ingest
endpoint. Today only the demo seeder writes a handful of `location_events`, so positions
decay to "Offline" within ~15 min and there is no engine/diagnostic stream at all.

## Options to make it live (pick one)

**A. Real hardware telematics provider (true Samsara parity).** Integrate a fleet
telematics API and forward its webhooks/polls into `/api/telemetry/ingest`:
- **Samsara** — `api.samsara.com`, OAuth 2.0 / API token, rich vehicle stats
  (GPS, engine RPM, fuel, DTCs, dashcam). Best fit for the OBD/J1939 + dashcam surfaces.
- **Motive (ex-KeepTruckin)** — strong ELD/HOS + GPS; good fit for the HOS/ELD module too.
- **Geotab** — `MyGeotab` SDK, deep J1939/OBD diagnostics.
  → **If you want this, create an account with one of the above and share API
  credentials (token + org/group id).** I'll build a small ingest worker
  (`backend-dotnet` background service or a Vercel cron function) that pulls the
  provider's vehicle stats on an interval and POSTs them to `/api/telemetry/ingest`,
  mapping their vehicle ids to OpsTrax `vehicles`. No frontend change needed — the
  existing pages already read the real tables.

**B. Simulator for demos (no external account, no cost).** A backend background service
that generates plausible movement/diagnostics for the demo tenant's vehicles and writes
real `location_events` + telemetry rows on a timer. Makes the maps and OBD screens live
for demos without hardware. This is the fastest path to "looks alive" and I can build it
without any credentials from you.

**C. Leave as-is.** Device registry, HOS, DVIR, work orders, safety, scorecards are all
real DB-backed already; only continuous GPS/engine *streams* are absent.

## Recommendation

For a **customer-facing Samsara-like experience**, do **A with Samsara or Motive** — it's
the only way the GPS/OBD/dashcam screens carry genuinely live vehicle data. For an
**impressive demo now**, do **B** (I can implement it this session on request). The two are
not mutually exclusive: B is a good stand-in until A's account/credentials are ready.
