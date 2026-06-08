# Attendance Device Integration — KynexOne

_How physical attendance devices, cloud platforms, and software punch sources connect to KynexOne, and how to configure them._

## Architecture: raw events → processing → attendance

```
                         ┌─────────────────────────── KynexOne API ───────────────────────────┐
 Device / Cloud / Agent  │  POST /api/attendance/ingest        (X-Device-Key header)            │
 ──── punches ──────────▶│        │                                                             │
                         │        ▼                                                             │
                         │  AttendanceRawEvent (immutable log)  ──▶  Processing engine          │
                         │   EmployeeCode, DeviceId, ts, dir,         (ProcessEmployeeDay):      │
                         │   geo, photo, confidence, payload          dedup → pair IN/OUT →      │
                         │                                            apply AttendancePolicy →   │
                         │                                            attendance_records +       │
                         │                                            daily record + exceptions  │
                         └──────────────────────────────────────────────────────────────────────┘
```
The **raw event store is the immutable source of truth** (audit/SOC). `attendance_records` is the derived, policy-applied result. Processing runs automatically on ingest (`autoProcess`) and can be re-run via `POST /api/attendance/process`.

## The 4 ingestion patterns (cover 100% of devices)

| # | Pattern | How | Use for |
|---|---------|-----|---------|
| A | **Push / webhook** | Device/cloud/agent POSTs to `/api/attendance/ingest` with `X-Device-Key` | Cloud devices, push-firmware biometrics, our agent |
| B | **Pull via local agent (KynexBridge)** | Agent on the branch LAN polls devices via vendor SDK, forwards to pattern A | On-prem biometric devices behind a firewall |
| C | **File / SFTP import** | `POST /api/attendance/events/import` (CSV) | Legacy/file-only devices (old eSSL/ZKTeco, Matrix exports) |
| D | **Software punch** | `/api/attendance/punch/{web,mobile,kiosk}` | Mobile GPS+geofence, kiosk/tablet, web |

## Device coverage matrix

| Device class | Examples | Pattern |
|---|---|---|
| Fingerprint | ZKTeco, eSSL, Suprema, Anviz, Matrix, Realtime | B, or A (push firmware) |
| Face | ZKTeco SpeedFace, Hikvision, Suprema FaceStation, Dahua | A (webhook/ISAPI) or B |
| Palm / Iris | ZKTeco palm, Iris ID | B |
| RFID / card | HID, Matrix, ZKTeco readers | B or C |
| Access control / turnstiles | Hikvision, Suprema BioStar 2, ZKAccess | A (webhook) |
| Cloud attendance | Jibble, Truein, ZKBio CVCloud | A (their webhook → ingest) |
| Mobile / field | drivers, field staff | D (GPS + geofence) |
| Kiosk / tablet | reception iPad | D (PIN/QR/selfie) |
| File-only / legacy | `.dat`/`.csv` exports | C |

## ✅ What's built (verified live)
- **Device registry** — `GET/POST/PUT/DELETE /api/attendance/devices`.
- **Per-device API key** — `POST /api/attendance/devices/{id}/generate-key` (returns plaintext **once**; only a SHA-256 hash is stored).
- **Generic webhook ingest** — `POST /api/attendance/ingest` authenticated by `X-Device-Key` (no user login). Accepts a **batch** of punches, dedups, stores raw events, and **auto-processes** affected days into `attendance_records`. Unmatched employee codes are accepted into the raw log and counted for mapping.
- **CSV import** — `POST /api/attendance/events/import`.
- **Software punch** — web / mobile (GPS) / kiosk.
- **Processing engine** — pairs IN/OUT, computes worked/late/early/overtime/undertime, sets status (Present/Late/Half-day/Absent), flags missing punches.
- **Monitoring** — `GET /api/attendance/devices/{id}/sync-logs`, `/reports/device-sync`, `/reports/missing-punch`, `/events/raw`.

### Example: a device (or the agent) pushing punches
```bash
curl -X POST https://<api>/api/attendance/ingest \
  -H "X-Device-Key: knx_xxxxxxxx..." \
  -H "Content-Type: application/json" \
  -d '{
    "punches": [
      {"employeeCode":"KNX-0001","punchTimestampUtc":"2026-06-08T05:02:00Z","punchDirection":"In","verificationMethod":"Face","confidenceScore":0.98},
      {"employeeCode":"KNX-0001","punchTimestampUtc":"2026-06-08T14:10:00Z","punchDirection":"Out","verificationMethod":"Face"}
    ],
    "autoProcess": true
  }'
# → {"received":2,"accepted":2,"duplicates":0,"unmatched":0,"processed":1,"syncLogId":"..."}
```

## Admin setup flow
1. **Attendance → Devices → Add device:** name, type, vendor, serial, branch/location, sync method (Push/Pull/File/Cloud), frequency.
2. **Generate device key** → copy the plaintext key (shown once) into the device/cloud/agent config.
3. **Map enrollment IDs → `EmployeeCode`** (punches carry `employeeCode`; unmatched land in the raw log for mapping).
4. Point the device/cloud webhook (or the agent) at `/api/attendance/ingest` with the `X-Device-Key` header. For file-only devices, schedule a CSV drop to `/events/import`.
5. **Monitor** sync-logs, device-sync and missing-punch reports.

## Security
- Per-device API key (SHA-256 hashed at rest); invalid/inactive key → `401`.
- Ingest is scoped to the device's tenant; the key identifies device + tenant (no cross-tenant write).
- HMAC/IP-allowlist and clock-sync checks recommended for production webhooks.
- Raw payloads retained for audit.

## 🔜 Roadmap (not yet built)
1. **KynexBridge local agent** (Windows service / Docker) with ZKTeco (PUSH/PULL SDK), Hikvision ISAPI, and Suprema BioStar 2 adapters → unlocks all on-prem biometric devices via Pattern B. The agent simply forwards to `/api/attendance/ingest`, so the cloud side is already done.
2. **Real `test-connection` / scheduled `sync`** for pull devices (today these write a sync-log placeholder).
3. **Vendor cloud connectors** (Jibble/Truein/ZKBio webhooks → normalized to ingest).
4. **Device-config & employee-enrollment-mapping UI** + unmatched-punch exception queue.
