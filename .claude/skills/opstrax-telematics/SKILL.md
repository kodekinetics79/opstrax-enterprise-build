---
name: opstrax-telematics
description: >
  Use when working on OpsTrax telematics, GPS/live-map, device ingest, or fleet
  telemetry. Triggers: "live map", "telematics", "GPS", "tracker", "PT40", "GT06",
  "telemetry ingest", "gps-ingest", "latest_vehicle_positions", "location_events",
  "eld_devices", "telemetry_gateways", "device commissioning", "breadcrumbs",
  "SSE stream", "vehicle not moving", "map shows offline", "freshness", "simulator",
  "why is the map empty", "500 on /api/vehicles", "schema drift". Also use before
  changing anything under backend-dotnet Telemetry*, telematics/, or tools/telematics/.
---

# OpsTrax Telematics

Hard-won operational knowledge. Every claim here was verified against the running
system, not inferred. Trust this over assumptions, but **re-verify before acting** —
production schema drifts.

## The five traps that will cost you hours

### 1. Enabling the simulator in Production/Staging makes the API refuse to boot
`Telemetry:Simulator:Enabled=true` is a **`fail`** in protected environments
(`ConfigValidationService.cs:224`), and `EnsureStartupAllowed` throws on any failure
(`:297-302`, called from `Program.cs:336`). The process dies at startup, and
`/health/ready` independently 503s. `isProtectedEnvironment` covers **Staging too**
(`:20-22`) — there is no "just use staging" escape. Never flip that flag on a
deployed service. Commit `f739055` exists because someone already did.

The sanctioned alternative, quoting the validator: *"protected environments must use
authenticated device/provider fixes only."*

### 2. Setting `Telemetry__GatewaySecret` also stops the API booting
The fleet-wide secret was removed as a cross-tenant skeleton key. Its **mere presence**
is a `fail` (`ConfigValidationService.cs:163-167`). Several older docs still tell you
to set it — they are wrong. Gateway credentials are per-gateway encrypted rows in
`telemetry_gateways`.

### 3. The first valid fix promotes the device to Active — and that can violate a CHECK
`GpsTrackerIngest` runs `status=CASE WHEN LOWER(status) IN ('provisioning','pending')
AND @hasProof THEN 'Active' END` (`EndpointMappings.cs:16857`), where `@hasProof` is
merely "a device timestamp was supplied" — which is mandatory, so it **always** fires.

`ck_eld_devices_active_credentials` and `ck_stage66_eld_active_credentials` then require
a 64-hex `api_key_hash`, a non-null `hmac_secret_encrypted`, `hmac_key_version > 0` and
`revoked_at IS NULL`. Both are `NOT VALID`, so existing rows are exempt but **the UPDATE
is checked**. A device without credentials therefore aborts the entire ingest transaction
on its first real fix, and the fix is silently lost.

**Always mint credentials before a device connects**, via
`POST /api/telemetry/devices/{id}/rotate-secret`. It cannot be done in SQL — the envelope
uses the API's `DATA_ENCRYPTION_KEY`.

### 4. Identity resolution demands EXACTLY one installation
`TelemetryIdentityResolution.cs:19-80` returns null (→ **422**, fix discarded) unless:
- device status ∈ `active|provisioning|pending` and state not suspended/quarantined/lost/retired
- **exactly one** `device_installations` row with `status IN ('Installed','Verified')`,
  `effective_from <= fixTime`, `effective_to IS NULL OR > fixTime`
- **at most one** matching `dispatch_assignments`

Zero installations and two installations both fail. A stock database seeds **zero**
`eld_devices` and **zero** `device_installations`, so a fresh environment cannot accept
a single fix until you provision both.

### 5. Ingest fails CLOSED in Production on schema drift
`GpsGatewayProjectionTopologyReadyAsync` (`EndpointMappings.cs:17063-17098`) requires 12
columns across 6 tables. Any one missing → **503 on every fix**, before credential
lookup. Also 503 if `PiiProtectionService.Enabled` is false, or the replay ledger probe
is anything but `Present`.

**Diagnostic that costs one request:** POST `/api/telemetry/gps-ingest` with a bogus
`X-Gateway-Id`.
- `401` → topology fine, you reached credential lookup. Good.
- `503` → failing closed. Check the 12 columns before anything else.
- `401` with no `X-Gateway-Id` header at all → handler reachable.

## Diagnosing "the map is empty / everything is broken"

Work outside-in. Do **not** start by reading frontend code.

1. `GET /health/ready` — `fleet_production_contract` counters tell you if schema drifted.
2. Hit the endpoints with a real token and record status codes. `live-map-summary` reads
   `telemetry_live_asset_states`; `positions` reads `latest_vehicle_positions`. One can
   work while the other 500s — that asymmetry localises the fault immediately.
3. **Get the actual Postgres error from Render logs** rather than guessing which column
   is missing:
   ```bash
   render workspace set <workspace-id> --confirm -o text
   render logs --resources <srv-id> --limit 30 --level error --output text --confirm
   ```
   `42703` = undefined column (schema drift). `42501` = RLS violation (tenant context
   not established). This turns a guessing game into a list.
4. Compare deployed commit (`deployment_version` in every log line, also `/health`)
   against `main`. If production runs code newer than its schema, expect exactly this.

**The migration ledger lies.** `schema_migrations` showed 27 unapplied migrations while
several of their tables already existed. Query `information_schema.columns` /
`to_regclass` directly — inspection is authoritative, the ledger is not.

## Freshness, and why a dot reads stale

`TelemetryPositions` labels `<=120s live`, `<=900s delayed`, else `stale`, using
**`GREATEST(receipt-age, device-fix-age)`** when provenance columns exist — so a
backdated or offline-buffered frame can never render as live. Any feed must tick faster
than **120s** to hold a fleet green.

`vehicles.device_status` is recomputed only every 5 minutes by
`TelemetryBackgroundService`, so a unit going offline lags up to ~10 minutes.

## Payload fields that actually matter

Required by `gps-ingest`: `imei` (or `X-Device-IMEI`), `lat`, `lng`, and a
device-originated timestamp (`gpsTime`/`ts`/…) — **rejected without it**.

| Field | Why it matters |
|---|---|
| `engineStatus` | The frontend buckets Moving via `/active\|on route\|moving\|driving\|en route/` **or** `speed > 3` (`LiveMapPage.tsx:131-143`). `"On"` matches **neither** — send `"Moving"`/`"Idle"`/`"Off"` or units read as Parked. |
| `harshEvent` + `magnitude` | The **only** producer of `safety_events` → driver scorecards. Without it Safety pages sit empty while the map looks alive. |
| `speedKmh`/`speedMph` | Speeding alerts vs `telemetry_rules`, trip compliance. |
| `provider`, `protocol` | Stamped into provenance; omitting them leaves the Fix Provenance drawer half-null. |
| `heading` | Marker rotation. Beware: `round(359.97,1)=360.0` is **rejected** (`heading >= 360` invalid). |

Never sent by the device: `driver_id`. Attribution comes from `dispatch_assignments`.

## Gateway HMAC contract

`X-Gateway-Id`, `X-Gateway-Timestamp` (±300s), `X-Gateway-Signature` =
lowercase-hex `HMAC-SHA256(secret, "{timestamp}.{rawBody}")`.

The server signs `body.GetRawText()` — **the exact bytes received**. Re-serialising the
JSON changes the signature. Verified byte-identical between Python `hmac` and .NET
`HMACSHA256`; if signatures mismatch, suspect your body bytes, not the algorithm.

Replay defence keys on the canonical HMAC **bytes**, durable via
`UNIQUE(gateway_id, signature)` — re-casing the hex does not bypass it, and identical
payload+timestamp cannot be sent twice.

## Provenance honesty

Fixes through `gps-ingest` land as `source='gps-tracker'`, `source_channel='trusted-gateway'`
— **indistinguishable from real hardware** — and they advance device lifecycle, writing
`activation_verified_at`, i.e. a commissioning record. The banned simulator was more
honest: it stamps `source='simulator'`.

So synthetic feeds belong on a **dedicated demo tenant**, never on tenants holding real
customer data. This is a judgement call to surface to the user, not to make silently.

## Tenant and geographic coherence

Always read the tenant's `country` / `currency` / `timezone` before generating any
location data. Seeding Virginia coordinates into a Toronto tenant (`CA`/`CAD`/
`America/Toronto`), or Riyadh coordinates into a US one, is the single fastest way to
look fake to a knowledgeable buyer — visible in seconds at street zoom.

Match status vocabularies to values already present in the database rather than inventing
them; several differ from the obvious guess (`sla_status` is `On Track|At Risk`, **not**
`On Time|Breached`; `stop_type` has no `Return`). Query `SELECT DISTINCT` first.

## Writing DDL against the live database

Adding a column looks harmless and is not. `ALTER TABLE` takes ACCESS EXCLUSIVE, and the
API routinely holds sessions `idle in transaction`; the ALTER then queues, and **a queued
exclusive request blocks every new reader behind it** — turning a column add into an
outage on the busiest table.

Always:
```sql
SET lock_timeout = '3s';   -- fail fast instead of queueing
```
and **no enclosing transaction** for multi-table column adds, so one blocked statement
neither rolls back the others nor holds their locks. With `ADD COLUMN IF NOT EXISTS`,
re-running converges. Nullable columns are catalog-only in PG11+ (no rewrite), so this is
safe on large tables — `location_events` routinely holds hundreds of thousands of rows.

If a statement keeps losing the race, do **not** raise the timeout. The API's background
workers hold transactions open for 80+ seconds (observed: a `maintenance_items` COUNT and
a `driver_safety_scores` INSERT), so retry in a loop with a short timeout instead — see
`tools/pt40/08-retry-locked-ddl.sh`. Restarting the Render service drops those connections
and frees the locks if retrying is not converging.

**After ADD COLUMN, check whether the table uses column-level grants.** `eld_devices` is
the one telemetry table where `opstrax_app` has *no* table-level SELECT — its ACL is
`{opstrax_app=awd}`, and read access is granted column by column so tenant reads can never
see HMAC/API-key material. Column grants do **not** extend to columns created later, so
new columns land unreadable and the endpoint starts returning
`42501: permission denied for table eld_devices` — a worse-looking error than the missing
column the ADD COLUMN just fixed. Recompute the grant with stage76's own rule ("every
column except the five secret-bearing ones"); `tools/pt40/09-eld-devices-column-grants.sql`
does exactly that and is self-correcting for future columns. Check `pg_class.relacl` before
assuming a table has table-level SELECT.

## The physical device path (PT40-Q / GT06)

- Decoder `Gt06Adapter` is complete: 39/39 protocol tests pass. A real login frame gets
  the byte-exact ACK `787805010001D9DC0D0A`.
- The gateway (`telematics/`) is a **separate process** speaking raw TCP on `:5023`, and
  writes to Postgres directly via `PostgresPositionProjectionStore` — it does not need
  `gps-ingest`.
- Render web services route HTTP only, so the TCP device edge **cannot** live there. Use
  Fly (raw TCP, no handlers) or a VPS. `telematics/fly.toml` is ready; build context must
  be the repo root.
- The protocol is *presumed* GT06 until a real captured frame is fingerprinted with
  `tools/telematics/fingerprint.py`. If it is not GT06, that adapter is unbuilt — treat
  as an open risk, never as done.

## Useful entry points

| Concern | Location |
|---|---|
| Route table | `EndpointMappings.cs:181-228` |
| Gateway ingest | `EndpointMappings.cs:16489` |
| Native device ingest | `EndpointMappings.cs:15611` |
| Identity resolution | `Controllers/TelemetryIdentityResolution.cs:19` |
| Positions read + freshness | `EndpointMappings.cs:16247` |
| Live map UI | `frontend/src/pages/LiveMapPage.tsx`, `components/LiveMap.tsx` |
| SSE hook | `frontend/src/hooks/useLiveTelemetry.ts` |
| Config gates | `Services/ConfigValidationService.cs` |
| Schema contract | `Services/FleetProductionReadinessService.cs` |
| Feeder + field tools | `tools/telematics/` |

## Working style for this area

State what you verified and what you assumed. "The 503 is gone, confirmed by probe" is
worth more than a paragraph of reasoning. When a subagent or a doc asserts something
about this subsystem, check it — several confident claims in this repo's own docs are
stale, and at least one would take production down if followed.
