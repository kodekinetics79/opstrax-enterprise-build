# Public TCP device edge — deployment runbook

The OpsTrax API runs on Render, which routes **HTTP/HTTPS only**. A PT40-class tracker does not
speak HTTP: it opens a raw TCP session to a fixed host:port and streams binary frames down it for
hours. Those two facts cannot be reconciled on Render, so the device edge is a separate process on
a host that can expose an arbitrary public TCP port.

This directory deploys that process onto a small VPS — AWS EC2, Lightsail, or DigitalOcean.

```
  PT40 / GT06 tracker
        │  raw TCP :5023, long-lived, binary
        ▼
  ┌──────────────────────────────────────────────┐
  │  VPS  ·  Opstrax.Telematics.Gateway          │
  │  static IP · no database credentials         │
  │                                              │
  │  protocol arbitration → IMEI allowlist       │
  │  → replay guard → normalize → HMAC sign      │
  │  → HTTPS ─────────────┐   (outbox on fail)   │
  └───────────────────────┼──────────────────────┘
                          │  POST /api/telemetry/gps-ingest
                          ▼
                   OpsTrax API (Render)
                   resolves device → tenant → vehicle
```

**The edge is a protocol translator; OpsTrax remains the identity authority.** Nothing on the VPS
resolves a tenant, company, vehicle or driver, and nothing on it touches Postgres. A box that every
scanner on the internet can reach has no business holding a database role, and a second source of
truth for tenancy is how one customer's trucks end up on another customer's map.

---

## What you need before starting

| Thing | Why |
|---|---|
| A VPS with a **static** public IP | The tracker's `SERVER` command bakes the address in. Changing it on a deployed unit means physical access or a carrier SMS. Attach an EC2 Elastic IP / Lightsail static IP / DigitalOcean reserved IP **before** configuring the device. |
| A gateway credential | `POST /api/telemetry/gateways` returns a secret **once**. It is bound to exactly one tenant, so this gateway cannot submit fixes for another tenant's device. |
| The device registered in `eld_devices` | OpsTrax resolves the IMEI to a company and vehicle. An unregistered IMEI is a 404 and the fix is dropped. |
| Its IMEI | For the edge allowlist. |

A 1 vCPU / 1 GB instance is ample: the process is I/O-bound on idle sockets, and the systemd unit
caps it at 512 MB.

---

## Deploy

### 1. Provision the host

Paste [`cloud-init.yaml`](cloud-init.yaml) into the instance's user-data / launch script. It
installs the firewall, disciplines the clock, tunes the socket limits, and creates the service
account and directories.

Then attach the static IP and confirm it:

```bash
ssh root@<ip> 'curl -fsS https://checkip.amazonaws.com'
```

### 2. Publish and copy the gateway

From the **repository root**:

```bash
dotnet publish telematics/src/Opstrax.Telematics.Gateway/Opstrax.Telematics.Gateway.csproj \
  -c Release -o ./publish

rsync -a ./publish/         root@<ip>:/opt/opstrax/gateway/
rsync -a telematics/deploy/ root@<ip>:/opt/opstrax/deploy/
```

### 3. Install the service

```bash
ssh root@<ip> '/opt/opstrax/deploy/install.sh \
  --gateway-id khalid-gw-1 \
  --base-url   https://opstrax-enterprise-build.onrender.com \
  --secret     "<the secret shown once at provisioning>"'
```

The gateway **refuses to boot** on unusable forwarding configuration — a non-`https` URL, a secret
under 32 characters, a missing gateway id. An edge that accepted tracker connections it could never
deliver would look healthy from outside while quietly filling its outbox.

### 4. Allowlist the device

```bash
ssh root@<ip> 'echo "862464068456321  # PT40-Q, tractor 118" >> /etc/opstrax/imei-allowlist.txt'
```

Re-read within ~15 seconds. No restart.

### 5. Point the device at the edge

Send the tracker its `SERVER` command with `<static-ip>:5023`, then confirm reachability from
outside first:

```bash
nc -vz <static-ip> 5023        # must connect
```

### 6. Watch the first real fix

```bash
ssh root@<ip> 'journalctl -u opstrax-telematics-gateway -f'
```

A healthy first session looks like:

```
Edge connection accepted from 203.0.113.0/24.
Edge connection 203.0.113.0/24 identified as GT06 (confidence 0.95).
Edge session 203.0.113.0/24 bound to allowlisted device 86***********21 via GT06.
```

Then check the live map. Note that `TelemetryPositions` grades a fix by
`GREATEST(receipt-age, device-fix-age)` — `<=120s` live, `<=900s` delayed, else stale. A feed
slower than 120s cannot hold a fleet green no matter how healthy the edge is.

---

## What the edge does to each frame

Every gate below discards traffic, and each one increments exactly one counter, so the counters
partition the loss rather than overlapping.

1. **Protocol arbitration.** Every installed adapter inspects the opening bytes; highest
   confidence wins. A tie is *refused*, not guessed — an ambiguous fingerprint decoded with the
   wrong vendor's field layout yields coordinates that are in range, plausible, and wrong, which
   nothing downstream can detect.
2. **IMEI allowlist.** Not authentication — an IMEI is a self-asserted, spoofable bearer
   identifier. What it buys is that only provisioned units get past the first frame, which keeps
   internet background noise off OpsTrax entirely. Empty allowlist admits nothing; unreadable
   allowlist file admits nothing and does *not* serve stale contents, so deleting a line is a real
   revocation.
3. **Malformed-frame rejection.** A CRC failure skips that frame without fabricating a fix.
   Unrecoverable framing (impossible length, destroyed stop bits) fails closed and drops that one
   connection — never the listener, never another device.
4. **Replay defence.** A byte-for-byte retransmission is suppressed at the edge and still
   acknowledged, so the device stops retrying. OpsTrax's durable ledger — keyed on the canonical
   HMAC signature, with `UNIQUE(gateway_id, signature)` — remains the authority across restarts
   and across multiple edges.
5. **Normalization.** Coordinates, the mandatory device clock, and the ±30-day/+5-minute window
   are validated here so a real fix is never lost to a formatting rejection. Out-of-range
   *auxiliary* readings (speed, fuel, odometer) are dropped and named in the log while the
   position still goes through.
6. **HMAC-signed HTTPS forward.** `HMAC-SHA256(secret, "{unixSeconds}.{rawBody}")`, lowercase hex,
   over the exact bytes sent. The identifier travels in the **signed body only** — the
   `X-Device-IMEI` header would sit outside the HMAC.
7. **Durable outbox on failure.** Crash-safe files on persistent disk. A frame is acknowledged
   only once it is delivered *or* durably parked; if it can be neither, the ack is withheld so the
   tracker retransmits.

---

## Operating it

```bash
systemctl status opstrax-telematics-gateway
journalctl -u opstrax-telematics-gateway -f
journalctl -u opstrax-telematics-gateway -p warning --since "1 hour ago"

ls /var/lib/opstrax-gateway/outbox | wc -l      # parked fixes; should be ~0 when healthy
```

### Reading the failures

| Symptom | Meaning | Action |
|---|---|---|
| `401` logged at **Critical**, outbox growing | Bad secret, revoked gateway row, **or clock skew** past ±300s | `timedatectl status` first — a drifted clock presents exactly like a bad credential |
| `403` | Device is quarantined, not enabled for telemetry, or belongs to a different tenant than this credential | Commissioning problem in OpsTrax, not on the edge |
| `404` | IMEI resolves to no device, or to more than one | Register it in `eld_devices` |
| `503` | OpsTrax ingest failing closed on schema topology or its replay ledger | Server-side; fixes park safely meanwhile |
| Refusals climbing, nothing delivered | IMEI not on the allowlist, or the allowlist file is unreadable | The log line reports `allowlist file UNREADABLE` when that is the cause |
| Connections accepted then immediately dropped | No installed adapter recognised the bytes | Capture the first frame and run `tools/telematics/fingerprint.py` |

### Rotating the gateway secret

```bash
# 1. POST /api/telemetry/gateways/{id}/rotate-secret   -> the previous secret dies immediately
ssh root@<ip> 'printf "Gateway__Edge__Forward__Secret=%s\n" "<new>" > /etc/opstrax/gateway.env \
  && chown root:opstrax /etc/opstrax/gateway.env && chmod 0640 /etc/opstrax/gateway.env \
  && systemctl restart opstrax-telematics-gateway'
```

Fixes arriving during the gap are parked and delivered on the next drain — the outbox is what makes
this a routine operation rather than a maintenance window.

---

## Deliberate constraints

- **No TLS terminator, HTTP proxy, or L7 load balancer in front of port 5023.** Anything that
  inspects or rewrites the stream corrupts the binary framing. This is also why the Fly config
  (`telematics/fly.toml`) declares no handlers.
- **Positionless heartbeats are acknowledged but not forwarded.** The trusted-gateway ingest
  contract requires a coordinate; there is no endpoint for a keepalive. They are counted
  (`HeartbeatsNotForwarded`) rather than being made to look like delivery.
- **One edge per gateway credential.** The credential is tenant-scoped. Two edges may share one
  credential — OpsTrax's durable replay ledger is shared — but each edge's local replay window and
  outbox are its own.
- **Pacific Track needs the vendor parser.** Until one is installed the PT adapter is registered
  fail-closed: PT devices are refused and counted, never handed to the GT06 adapter as a fallback.
  See [`../src/Opstrax.Telematics.Protocols.PacificTrack/README.md`](../src/Opstrax.Telematics.Protocols.PacificTrack/README.md).

## Alternatives to a VPS

- **Fly.io** — [`../fly.toml`](../fly.toml) is ready and gives raw TCP with no handlers. Fewer
  moving parts than a VPS, but it is configured for the *direct-Postgres* topology; set
  `Gateway__Edge__Egress=Https` to run it as a credential-free forwarding edge instead.
- **Docker on any host** — [`docker-compose.yml`](docker-compose.yml). Convenient where Docker is
  already running, though the systemd unit sandboxes the process more tightly than a default
  container does.
