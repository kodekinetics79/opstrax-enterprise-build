# PT40-Q go-live — step 02: credentials, gateway host, device repoint

Run [`01-schema-and-device.sql`](01-schema-and-device.sql) first. It repairs the schema and
un-revokes the device, but it deliberately stops short of two things SQL must not do.

Facts this runbook is built on (verified against production, not assumed):

| Thing | State |
|---|---|
| Tenant | `KHALID-DEMO`, `company_id = 8` |
| Vehicle | `1024` — `KHALID-PILOT-01`, "PT40-Q Pilot Asset" |
| Device row | `eld_devices.id = 1011`, IMEI `862464068456321`, serial `4C4000067803` |
| Deployed API | commit `42f5890` — identical to `main` |
| API base | `https://osptrax-fleet-management.onrender.com` |

---

## 1. Mint device credentials — **required before the device ever connects**

The ingest handler promotes `provisioning → Active` on the **first** valid fix. Both
`ck_eld_devices_active_credentials` and `ck_stage66_eld_active_credentials` then demand a
64-hex `api_key_hash`, a non-null envelope-encrypted HMAC secret, `hmac_key_version > 0`
and `revoked_at IS NULL`. Device 1011 currently has none of those.

Because the constraints are `NOT VALID`, existing rows are exempt but **that UPDATE is
checked** — so without this step the first real fix from the tracker aborts the whole
ingest transaction and the fix is lost.

This cannot be done in SQL: the envelope is produced with the API's `DATA_ENCRYPTION_KEY`,
which lives only in the running service. Hand-written values would either fail the
constraint or produce an envelope the app cannot decrypt.

```bash
API=https://osptrax-fleet-management.onrender.com

# Sign in as a KHALID-DEMO admin holding telemetry.devices.manage
TOKEN=$(curl -s -X POST "$API/api/auth/login" -H 'Content-Type: application/json' \
  -d '{"companyCode":"KHALID-DEMO","email":"<admin-email>","password":"<password>"}' \
  | python3 -c 'import sys,json; print(json.load(sys.stdin)["data"]["token"])')

curl -s -X POST "$API/api/telemetry/devices/1011/rotate-secret" \
  -H "Authorization: Bearer $TOKEN"
```

**Gate:** re-run the verification query at the bottom of `01-…sql` and confirm
`has_enc_secret = t` and `has_api_key = t`.

---

## 2. Provision the gateway credential

Only needed for the **HTTP forwarder** path (`/api/telemetry/gps-ingest`). If the PT40
talks straight to the TCP gateway, that service writes to Postgres directly and this
credential is not on the critical path — but provision it anyway: it is also what
[`live_feed.py`](../telematics/live_feed.py) uses as the demo-day fallback.

```bash
curl -s -X POST "$API/api/telemetry/gateways" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"gatewayId":"pt40-edge-1","name":"PT40 field edge"}'
```

The secret is returned **once**. Store it immediately:

```bash
export OPSTRAX_GATEWAY_SECRET='<secret from the response>'
```

Note the numeric `id` in the response too — it is what the kill switch takes:

```bash
# Kill switch. Ingest 401s before any DB write; works even if the feeder host is gone.
python3 tools/telematics/live_feed.py revoke --api "$API" --gateway-row-id <id>
```

> **Never set `Telemetry__GatewaySecret`.** The fleet-wide secret was removed; its mere
> presence is a `fail` in Production and **stops the API booting**. Some older docs in this
> repo still reference it — they are wrong.

---

## 3. Deploy the TCP gateway (Fly)

Render web services route HTTP/HTTPS only. A GT06 tracker opens a **raw TCP** session, so
the device edge needs a host that exposes a bare port. The API stays on Render — only this
one container moves.

```bash
# once, if you don't have it
brew install flyctl          # or: curl -L https://fly.io/install.sh | sh
fly auth login

# from the REPOSITORY ROOT -- the Dockerfile COPYs telematics/src/..., so the build
# context must be the repo root, not the telematics/ directory
fly launch --no-deploy --copy-config --config telematics/fly.toml

fly secrets set --config telematics/fly.toml \
  ConnectionStrings__Telematics="$NEON_PG_URI" \
  ConnectionStrings__PlatformRegistry="$NEON_PG_URI"

fly deploy --config telematics/fly.toml
fly ips list        # -> the public IPv4 that goes in the SERVER command below
```

Both connection strings are mandatory: `Program.cs` refuses to start in Production without
them. `fly launch` may offer to add a Postgres or tweak the config — decline; the config is
already written, and the database is Neon.

**Gate — confirm the edge is reachable before touching the device:**

```bash
nc -vz <public-ip> 5023        # must connect
fly logs --config telematics/fly.toml
# expect: "Telematics gateway listening on 0.0.0.0:5023 (protocol GT06, ...)"
# and a WARNING that it is bound to a non-loopback interface -- that warning is correct here
```

---

## 4. Repoint the tracker

Needs an active data SIM. Send via SMS to the SIM, or the vendor tool:

```
SERVER,1,<public-ip>,5023,0#
APN,<carrier-apn>#
```

`Code: 226660` on the device label is most likely the command password — confirm the exact
syntax against the PT40-Q command sheet before sending. Wrong syntax is usually ignored
silently rather than NAK'd.

**Gate:** the unit ACKs, and `fly logs` shows an accepted TCP connection.

---

## 5. Fingerprint the first frame — the one real unknown

"PT40-Q" is *presumed* GT06; that has never been proven for this unit. Capture the first
frame and classify it:

```bash
python3 tools/telematics/fingerprint.py <captured-frame-file>
```

- **GT06 confirmed** → nothing more to build. The decoder passes 39/39 tests and answers a
  real login frame with the byte-exact ACK (`787805010001D9DC0D0A`), verified locally.
- **Anything else** (JT808 / Teltonika / Queclink / …) → that adapter **does not exist yet**.
  This is the single genuinely unbounded risk in the plan; nothing can retire it until a
  real packet arrives.

---

## 6. Acceptance gate

Raw frame → decode → `latest_vehicle_positions` → `GET /api/telemetry/positions` → the
marker for `KHALID-PILOT-01` moves, with `freshness = 'live'` and a direct-device `source`.

```bash
curl -s "$API/api/telemetry/positions" -H "Authorization: Bearer $TOKEN" \
  | python3 -m json.tool | grep -A4 'KHALID-PILOT-01'
```

Per the repo's own standard this is `VERIFIED_PHYSICAL_DEVICE_TELEMETRY`: the marker moved
on a genuine physical fix, provably not simulator or seed. Do not substitute a hand-inserted
row to make it look alive — that fails the gate by construction.

---

## Still open after this

- `/health/ready` stays `not_ready` until the **grant** violations are also fixed (8 grant +
  19 tenant-grant, from unapplied stage76). Ingest and the map work regardless; readiness
  affects Render's health routing, so treat it as a follow-up, not a blocker.
- 27 migrations remain unapplied, including GL/tax/billing/MFA. Schedule that as its own
  exercise with a Neon branch/PITR checkpoint — **not** during demo week.
