# PT40-Q Pilot — Go-Live Checklist (Khalid)

Turnkey, ordered checklist to take the client PT40-Q from "catalogued" to "one genuine,
decoded fix on the live map." Every code prerequisite is DONE and tested; what remains is
**ops + device + DB access**. Do the steps in order. Identifiers are held exactly in your
secure notes — `IMEI 862464068456321`, `serial 4C4000067803`.

Deep background: [pt40-onboarding-runbook.md](pt40-onboarding-runbook.md) (18-step detail),
[pt40-fingerprint.md](pt40-fingerprint.md) (protocol decision tree),
[../FINDINGS_LEDGER.md](../FINDINGS_LEDGER.md) (TEL-P1-PT40-001).

---

## Status of code prerequisites (verified 2026-07-14)

| Piece | State |
|---|---|
| GT06 decoder (`Gt06Adapter` v1.0.0) | ✅ implemented + tested (30 cases) — ready IF the device fingerprints GT06 |
| TCP gateway (`TcpGatewayService`) | ✅ real listener; **now deployable** — `Gateway:ListenAddress` config (default loopback, set `0.0.0.0` to expose) |
| Projection → map | ✅ `PostgresPositionProjectionStore` writes the same `latest_vehicle_positions` the map reads |
| Live-map truth (freshness/provenance) | ✅ honest — a backdated/stale fix cannot render "live" (TEL-P1-TRUTH-003) |
| Ingest auth (HMAC gateway forward) | ✅ `POST /api/telemetry/gps-ingest`, IMEI = lookup only |

The remaining blockers are **not code.** They are the four ops actions below.

---

## Step 1 — Register the device in the production DB → Khalid's tenant  *(needs prod DB)*

The exact IMEI/serial are in **no** migration or seed — the device must be registered in
prod. First find Khalid's `company_id` and a vehicle in that company:

```sql
-- Khalid's company (adjust the name/email match to your tenant record)
SELECT id, name FROM companies WHERE name ILIKE '%khalid%';           -- → :company_id
SELECT id, vehicle_code FROM vehicles WHERE company_id = :company_id;  -- pick one → :vehicle_id
```

Then upsert the device row (protocol deliberately left blank — it is filled from the first
real capture, never the model name):

```sql
INSERT INTO eld_devices (device_serial, imei, device_model, company_id, vehicle_id, status)
VALUES ('4C4000067803', '862464068456321', 'PT40-Q', :company_id, :vehicle_id, 'provisioning')
ON CONFLICT (device_serial) DO UPDATE
  SET imei = EXCLUDED.imei, device_model = EXCLUDED.device_model,
      company_id = EXCLUDED.company_id, vehicle_id = EXCLUDED.vehicle_id;
```

**Gate:** exactly one non-deleted row resolves for `imei='862464068456321'`, bound to
Khalid's `company_id` and a real `vehicle_id`. `status` in (`active`,`provisioning`,`pending`)
so ingest will accept it (a valid signed fix flips `provisioning → Active`).

## Step 2 — Deploy the gateway on a reachable host  *(ops/infra — was root blocker #1)*

Run `Opstrax.Telematics.Gateway` on a public host with:

```json
// appsettings.json (or env)
"Gateway": { "ListenAddress": "0.0.0.0", "ListenPort": 5023 }
```

- `ListenAddress: 0.0.0.0` makes it a reachable device edge (it logs a WARNING confirming
  non-loopback bind). Leave it at the `127.0.0.1` default and the device can never reach it.
- Provide the projection DB connection (same Neon Postgres) and a `Telemetry:GatewaySecret`
  ≥ 32 chars if it forwards to `gps-ingest`.
- Open the firewall to the tracker's carrier IP range on `:5023` only.

**Gate:** `telnet <public-host> 5023` from outside connects; the gateway logs the accept.

## Step 3 — Repoint the device to the gateway  *(device/SIM — was root blocker #2)*

Send the PT40-Q's SIM the vendor server/APN command (SMS or vendor tool):

```
SERVER,1,<public-host>,5023,0#     (exact syntax per the unit's command set)
APN,<carrier-apn>#
```

**Gate:** the unit ACKs the new destination and opens a TCP session to `<public-host>:5023`.

## Step 4 — Capture, fingerprint, verify end-to-end

1. On first connect, capture the raw first frame (gateway framing buffer or `tcpdump`).
2. Fingerprint it with the decision tree / `tools/telematics/fingerprint.py`. **If it
   confirms GT06, the decoder is ready — no new code.** If not GT06, that branch's adapter
   is the one remaining build (JT808/Teltonika/Queclink/etc. are targets, not yet built).
3. Confirm the fix flows: raw frame → decode → `CanonicalTelemetryEvent` → projection →
   `latest_vehicle_positions` → `/api/telemetry/positions` → map marker for Khalid's vehicle.
4. Verify honesty: `source` shows a direct-device value, freshness is `live` only while the
   **device** fix is current, and one correlation id threads the raw frame → DB row → stream.

**Gate (VERIFIED_PHYSICAL_DEVICE_TELEMETRY):** the map marker moves on a *new physical
fix*, provably not simulator/seed/manual, traceable end-to-end by one correlation id.

---

## What I (engineering) cannot do from here, and why

- **Prod DB registration** (Step 1) needs production database access — not available in the
  build environment.
- **Public host** (Step 2) needs an infra/deploy decision (which host, DNS, firewall).
- **Device repoint** (Step 3) needs the SIM's SMS command channel and physical device.
- **Protocol confirmation** (Step 4) needs one real captured packet — cannot be inferred
  from "PT40-Q" alone.

Until Step 4's gate passes, the honest classification stays **UNVERIFIED — BLOCKED_EXTERNAL**.
Do not use the simulator or a hand-inserted row to make the map look alive for this unit; it
fails the acceptance gate by construction.
