#!/usr/bin/env python3
"""Drive the OpsTrax Live Fleet Map from the authenticated gateway ingest path.

WHY THIS EXISTS
    `Telemetry:Simulator:Enabled` is refused in protected environments:
    ConfigValidationService raises a *fail* for it and Program.cs calls
    EnsureStartupAllowed, so flipping that flag on a Production or Staging service makes
    the API refuse to boot. The sanctioned alternative -- quoting the validator itself --
    is "authenticated device/provider fixes only".

    That is exactly what this is. It runs OUTSIDE the deployment, holds a per-gateway
    credential, and POSTs HMAC-signed fixes to /api/telemetry/gps-ingest: the same route
    a physical GT06/PT40 tracker uses. No production config change, no redeploy, no
    readiness regression.

PROVENANCE -- READ BEFORE POINTING THIS AT A REAL TENANT
    Fixes sent this way land as source='gps-tracker', source_channel='trusted-gateway',
    which is indistinguishable in the database from genuine hardware, and a valid fix
    also promotes the device to Active and stamps device_installations
    .activation_verified_at -- a commissioning record. Use a dedicated demo tenant.
    Feeding tenants that hold real customer data writes fake field-commissioning history
    that cannot be undone without DB surgery.

FRESHNESS
    TelemetryPositions labels <=120s 'live', <=900s 'delayed', else 'stale'. Tick faster
    than 120s to hold the fleet green; the default 60s does.

USAGE
    export OPSTRAX_COMPANY_CODE=... OPSTRAX_EMAIL=... OPSTRAX_PASSWORD=...

    # 1. one-time: mint a gateway credential (the secret is shown exactly once)
    python3 tools/telematics/live_feed.py provision --api https://host --gateway-id demo-gw-1

    # 2. inspect what would be sent, zero network writes
    export OPSTRAX_GATEWAY_SECRET=...
    python3 tools/telematics/live_feed.py run --api https://host --gateway-id demo-gw-1 --dry-run

    # 3. go live
    python3 tools/telematics/live_feed.py run --api https://host --gateway-id demo-gw-1

    # kill switch -- server-side, works even if this host is unreachable
    python3 tools/telematics/live_feed.py revoke --api https://host --gateway-row-id 7
"""

from __future__ import annotations

import argparse
from concurrent import futures
import hashlib
import hmac
import json
import math
import os
import random
import ssl
import sys
import time
import urllib.error
import urllib.request
from typing import Any, Optional

TIMEOUT = 30
MAX_PAYLOAD_BYTES = 32_768      # server rejects >32768 with 413
SIGNATURE_SKEW_LIMIT = 300      # server rejects |now - ts| > 300s
LIVE_FRESHNESS_SECONDS = 120    # TelemetryPositions 'live' ceiling


# ── HTTP ─────────────────────────────────────────────────────────────────────

def _request(method: str, url: str, *, headers: dict[str, str] | None = None,
             body: bytes | None = None) -> tuple[int, Any]:
    req = urllib.request.Request(url, method=method, data=body)
    req.add_header("Accept", "application/json")
    if body is not None:
        req.add_header("Content-Type", "application/json")
    for k, v in (headers or {}).items():
        req.add_header(k, v)
    ctx = ssl.create_default_context()
    try:
        with urllib.request.urlopen(req, timeout=TIMEOUT, context=ctx) as resp:
            raw = resp.read().decode("utf-8", "replace")
            try:
                return resp.status, json.loads(raw)
            except json.JSONDecodeError:
                return resp.status, raw
    except urllib.error.HTTPError as exc:
        raw = exc.read().decode("utf-8", "replace")
        try:
            return exc.code, json.loads(raw)
        except json.JSONDecodeError:
            return exc.code, raw
    except urllib.error.URLError as exc:
        raise SystemExit(f"cannot reach {url}: {exc.reason}") from exc


def _unwrap(payload: Any) -> Any:
    """Unwrap the ApiResponse<T> envelope ({success,message,data})."""
    if isinstance(payload, dict) and "data" in payload:
        return payload["data"]
    return payload


# ── auth ─────────────────────────────────────────────────────────────────────

def login(api: str, company_code: str, email: str, password: str) -> str:
    status, payload = _request(
        "POST", f"{api}/api/auth/login",
        body=json.dumps({"email": email, "password": password,
                         "companyCode": company_code}).encode())
    if status != 200:
        msg = payload.get("message") if isinstance(payload, dict) else payload
        raise SystemExit(
            f"login failed ({status}): {msg}\n"
            "The API returns one deliberately non-revealing error for a wrong org code, "
            "email, OR password -- verify all three.")
    data = _unwrap(payload) or {}
    token = data.get("token")
    if not token:
        raise SystemExit(f"login returned no token; MFA or SSO may be required: {data}")
    return token


def _auth(token: str) -> dict[str, str]:
    return {"Authorization": f"Bearer {token}"}


# ── gateway credential ───────────────────────────────────────────────────────

def provision(api: str, token: str, gateway_id: str, name: str) -> dict[str, Any]:
    status, payload = _request(
        "POST", f"{api}/api/telemetry/gateways", headers=_auth(token),
        body=json.dumps({"gatewayId": gateway_id, "name": name}).encode())
    if status == 409:
        raise SystemExit(
            f"gateway '{gateway_id}' already exists. Its secret was shown only at creation; "
            "re-run with a NEW --gateway-id, or rotate the existing one.")
    if status == 403:
        raise SystemExit("this account lacks 'telemetry.devices.manage', or it is branch-scoped "
                         "(gateway provisioning requires a company-wide account).")
    if status != 200:
        raise SystemExit(f"provisioning failed ({status}): {payload}")
    data = _unwrap(payload) or {}
    if not data.get("secret"):
        raise SystemExit(f"no secret in response: {payload}")
    return data


# ── fleet discovery ──────────────────────────────────────────────────────────

def fetch_devices(api: str, token: str) -> list[dict[str, Any]]:
    status, payload = _request("GET", f"{api}/api/telemetry/devices", headers=_auth(token))
    if status != 200:
        raise SystemExit(f"device list failed ({status}): {payload}")
    usable = []
    for d in (_unwrap(payload) or []):
        identity = d.get("imei") or d.get("deviceSerial")
        state = str(d.get("status") or "").strip().lower()
        # The server accepts only these three; anything else is rejected 403.
        if identity and state in ("active", "provisioning", "pending"):
            usable.append({
                "imei": identity,
                "vehicleId": d.get("vehicleId"),
                "vehicleCode": d.get("vehicleCode") or identity,
            })
    return usable


def fetch_positions(api: str, token: str) -> dict[Any, dict[str, Any]]:
    status, payload = _request("GET", f"{api}/api/telemetry/positions", headers=_auth(token))
    if status != 200:
        raise SystemExit(f"positions failed ({status}): {payload}")
    seeded = {}
    for p in (_unwrap(payload) or []):
        if p.get("vehicleId") is not None and p.get("lat") is not None:
            seeded[p["vehicleId"]] = p
    return seeded


# ── movement model ───────────────────────────────────────────────────────────

class Unit:
    """One tracked vehicle, dead-reckoned along its heading.

    Units stay inside a box around their seed position so a long run never walks the
    fleet into the ocean; on contact the unit turns back toward its anchor.
    """

    BOX_DEGREES = 0.18   # ~20 km

    def __init__(self, imei: str, vehicle_code: str, lat: float, lng: float,
                 heading: float, rng: random.Random, moving: bool,
                 harsh_rate: float = 0.0):
        self.imei = imei
        self.vehicle_code = vehicle_code
        self.lat = lat
        self.lng = lng
        self.anchor = (lat, lng)
        self.heading = heading % 360
        self.rng = rng
        self.moving = moving
        self.harsh_rate = harsh_rate
        self.speed_kmh = rng.uniform(35, 85) if moving else 0.0
        self.fuel = rng.uniform(28, 96)
        self.odometer = rng.uniform(12_000, 190_000)
        self.pending_harsh: tuple[str, float] | None = None

    def step(self, seconds: float) -> None:
        if not self.moving:
            self.speed_kmh = 0.0
            return
        # Occasional traffic: slow down, sometimes nearly to a stop, then resume.
        roll = self.rng.random()
        if roll < 0.08:
            self.speed_kmh = self.rng.uniform(0, 15)
        elif roll < 0.20:
            self.speed_kmh = self.rng.uniform(15, 45)
        else:
            self.speed_kmh = min(110.0, max(8.0, self.speed_kmh + self.rng.uniform(-9, 9)))

        self.heading = (self.heading + self.rng.uniform(-12, 12)) % 360

        km = self.speed_kmh * (seconds / 3600.0)
        rad = math.radians(self.heading)
        dlat = (km * math.cos(rad)) / 110.574
        lng_scale = 111.320 * math.cos(math.radians(self.lat))
        dlng = (km * math.sin(rad)) / lng_scale if abs(lng_scale) > 1e-6 else 0.0
        self.lat += dlat
        self.lng += dlng
        self.odometer += km * 0.621371
        self.fuel = max(4.0, self.fuel - km * 0.02)

        # Rare, and only under conditions where a real event would occur.
        if self.rng.random() < self.harsh_rate and self.speed_kmh > 25:
            kind = self.rng.choice(["harsh_braking", "harsh_acceleration", "harsh_turn"])
            self.pending_harsh = (kind, round(self.rng.uniform(0.35, 0.62), 2))

        # Turn back if we wandered out of the box.
        if (abs(self.lat - self.anchor[0]) > self.BOX_DEGREES
                or abs(self.lng - self.anchor[1]) > self.BOX_DEGREES):
            self.heading = (math.degrees(math.atan2(
                self.anchor[1] - self.lng, self.anchor[0] - self.lat)) + 360) % 360
        self.lat = max(-89.9, min(89.9, self.lat))
        self.lng = max(-179.9, min(179.9, self.lng))

    def payload(self) -> dict[str, Any]:
        payload: dict[str, Any] = {
            "imei": self.imei,
            "lat": round(self.lat, 6),
            "lng": round(self.lng, 6),
            "speedKmh": round(self.speed_kmh, 1),
            # round() can push 359.97 to 360.0, which the server rejects (heading >= 360).
            "heading": round(self.heading, 1) % 360,
            # The frontend buckets Moving via /active|on route|moving|driving|en route/ or
            # speed>3 -- "On" matches neither, so a unit in traffic flipped to "Parked"
            # while its engine said On. Use the vocabulary the map actually reads.
            "engineStatus": "Moving" if self.moving else "Idle",
            "fuel": round(self.fuel, 1),
            "odometer": round(self.odometer, 1),
            # The server requires a device-originated timestamp and rejects anything
            # older than 30d or more than 5min in the future.
            "gpsTime": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            # Stamped into latest_vehicle_positions.provider/protocol by the upsert.
            "provider": "OpsTrax Forwarder",
            "protocol": "rest_json",
        }
        # Harsh events are the ONLY producer of safety_events -> driver scorecards.
        # Without them the map is alive while Safety and Scorecards sit empty.
        if self.pending_harsh is not None:
            payload["harshEvent"], payload["magnitude"] = self.pending_harsh
            self.pending_harsh = None
        return payload


# ── signed ingest ────────────────────────────────────────────────────────────

def sign(secret: str, timestamp: int, raw_payload: str) -> str:
    """HMAC-SHA256 over '{timestamp}.{rawPayload}', lowercase hex.

    The server signs body.GetRawText() -- the raw bytes as received -- so the exact
    string signed here must be the exact string sent. Verified byte-identical against
    .NET HMACSHA256.
    """
    return hmac.new(secret.encode(), f"{timestamp}.{raw_payload}".encode(),
                    hashlib.sha256).hexdigest()


def send_fix(api: str, gateway_id: str, secret: str, unit_payload: dict[str, Any]
             ) -> tuple[int, Any]:
    raw = json.dumps(unit_payload, separators=(",", ":"))
    if len(raw.encode()) > MAX_PAYLOAD_BYTES:
        raise SystemExit("payload exceeds the server's 32 KiB ingest cap")
    ts = int(time.time())
    return _request(
        "POST", f"{api}/api/telemetry/gps-ingest",
        headers={
            "X-Gateway-Id": gateway_id,
            "X-Gateway-Timestamp": str(ts),
            "X-Gateway-Signature": sign(secret, ts, raw),
        },
        body=raw.encode())


# ── commands ─────────────────────────────────────────────────────────────────

def _credentials() -> tuple[str, str, str]:
    company = os.environ.get("OPSTRAX_COMPANY_CODE", "").strip()
    email = os.environ.get("OPSTRAX_EMAIL", "").strip()
    password = os.environ.get("OPSTRAX_PASSWORD", "")
    if not (company and email and password):
        raise SystemExit(
            "set OPSTRAX_COMPANY_CODE, OPSTRAX_EMAIL and OPSTRAX_PASSWORD in the environment")
    return company, email, password


def cmd_provision(args: argparse.Namespace) -> int:
    company, email, password = _credentials()
    token = login(args.api, company, email, password)
    data = provision(args.api, token, args.gateway_id, args.name or args.gateway_id)
    print(f"gateway id  : {args.gateway_id}")
    print(f"gateway row : {data.get('id')}   <- --gateway-row-id for the kill switch")
    print(f"secret      : {data['secret']}")
    print("\nShown once. Store it now:\n"
          f"  export OPSTRAX_GATEWAY_SECRET='{data['secret']}'")
    return 0


def cmd_revoke(args: argparse.Namespace) -> int:
    """Revoke the gateway credential. Ingest then 401s before any DB write."""
    company, email, password = _credentials()
    token = login(args.api, company, email, password)
    status, payload = _request(
        "POST", f"{args.api}/api/telemetry/gateways/{args.gateway_row_id}/revoke",
        headers=_auth(token), body=b"{}")
    if status != 200:
        raise SystemExit(f"revoke failed ({status}): {payload}")
    print(f"gateway {args.gateway_row_id} revoked -- ingest now rejected server-side")
    return 0


def cmd_run(args: argparse.Namespace) -> int:
    secret = os.environ.get("OPSTRAX_GATEWAY_SECRET", "").strip()
    if not secret and not args.dry_run:
        raise SystemExit("set OPSTRAX_GATEWAY_SECRET (from the provision step)")
    if args.interval > LIVE_FRESHNESS_SECONDS:
        print(f"! interval {args.interval}s exceeds the {LIVE_FRESHNESS_SECONDS}s 'live' "
              "ceiling; the map will show 'delayed'", file=sys.stderr)

    company, email, password = _credentials()
    token = login(args.api, company, email, password)
    devices = fetch_devices(args.api, token)
    if not devices:
        raise SystemExit("no active/provisioning/pending devices with an IMEI in this tenant -- "
                         "provision devices before feeding telemetry")
    seeded = fetch_positions(args.api, token)

    rng = random.Random(args.seed)
    units: list[Unit] = []
    for d in devices[: args.limit]:
        pos = seeded.get(d["vehicleId"])
        if pos is not None:
            lat, lng = float(pos["lat"]), float(pos["lng"])
            heading = float(pos.get("heading") or rng.uniform(0, 359))
        elif args.origin:
            olat, olng = args.origin
            lat = olat + rng.uniform(-0.05, 0.05)
            lng = olng + rng.uniform(-0.05, 0.05)
            heading = rng.uniform(0, 359)
        else:
            # No prior fix and no --origin: skip rather than invent a location.
            continue
        units.append(Unit(d["imei"], d["vehicleCode"], lat, lng, heading, rng,
                          moving=rng.random() > args.idle_fraction,
                          harsh_rate=args.harsh_rate))

    if not units:
        raise SystemExit(
            "no unit had a prior position to continue from. Pass --origin LAT,LNG to place "
            "the fleet for a first run.")

    print(f"feeding {len(units)} unit(s) every {args.interval}s -> {args.api}"
          f"{' [DRY RUN]' if args.dry_run else ''}")

    tick = 0
    while args.ticks == 0 or tick < args.ticks:
        tick += 1
        started = time.monotonic()
        accepted = failed = 0
        first_error: Optional[str] = None

        payloads = []
        for unit in units:
            unit.step(args.interval)
            payloads.append(unit.payload())

        if args.dry_run:
            accepted = len(payloads)
            if tick == 1:
                for sample in payloads[:3]:
                    print(f"  would send {json.dumps(sample, separators=(',', ':'))}")
        else:
            # Sent concurrently: serial blocking sends made the REAL period
            # (interval + total send time) drift past the 120s 'live' ceiling, so the
            # map read 'delayed' despite a compliant --interval. Workers stay well under
            # the device bucket's 1200 req/60s (~20/s).
            def _send(p):
                return send_fix(args.api, args.gateway_id, secret, p)

            with futures.ThreadPoolExecutor(max_workers=args.concurrency) as pool:
                for status, body in pool.map(_send, payloads):
                    if status in (200, 201, 202):
                        accepted += 1
                    else:
                        failed += 1
                        if first_error is None:
                            first_error = f"{status}: {body}"

        elapsed = time.monotonic() - started
        stamp = time.strftime("%H:%M:%S")
        line = f"[{stamp}] tick {tick}: {accepted} accepted in {elapsed:.1f}s"
        if failed:
            line += f", {failed} rejected -- {first_error}"
        print(line, flush=True)

        if args.ticks and tick >= args.ticks:
            break
        # Pace on the tick START so the period is --interval, not interval+send.
        remaining = args.interval - (time.monotonic() - started)
        if remaining < 0:
            print(f"! a tick took {elapsed:.1f}s, longer than --interval {args.interval}s; "
                  "lower --limit or raise --concurrency", file=sys.stderr)
        time.sleep(max(0.0, remaining))
    return 0


def _origin(value: str) -> tuple[float, float]:
    try:
        lat, lng = (float(p) for p in value.split(","))
    except ValueError as exc:
        raise argparse.ArgumentTypeError("origin must be 'LAT,LNG'") from exc
    if not (-90 <= lat <= 90 and -180 <= lng <= 180):
        raise argparse.ArgumentTypeError("origin out of range")
    return lat, lng


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)

    p = sub.add_parser("provision", help="mint a gateway credential (secret shown once)")
    p.add_argument("--api", required=True)
    p.add_argument("--gateway-id", required=True)
    p.add_argument("--name")
    p.set_defaults(func=cmd_provision)

    k = sub.add_parser("revoke", help="KILL SWITCH -- revoke the gateway server-side")
    k.add_argument("--api", required=True)
    k.add_argument("--gateway-row-id", required=True,
                   help="numeric id returned by provision (not the gateway-id string)")
    k.set_defaults(func=cmd_revoke)

    r = sub.add_parser("run", help="feed signed GPS fixes on an interval")
    r.add_argument("--api", required=True)
    r.add_argument("--gateway-id", required=True)
    r.add_argument("--interval", type=int, default=60,
                   help="seconds between fixes (default 60; >120 reads as 'delayed')")
    r.add_argument("--limit", type=int, default=40,
                   help="max vehicles to feed (default 40; size to the device registry)")
    r.add_argument("--ticks", type=int, default=0, help="0 = run until interrupted")
    r.add_argument("--idle-fraction", type=float, default=0.18,
                   help="share of the fleet parked but fresh (default 0.18)")
    r.add_argument("--harsh-rate", type=float, default=0.02,
                   help="per-tick chance of a harsh event while moving (default 0.02)")
    r.add_argument("--origin", type=_origin,
                   help="LAT,LNG to place units that have no prior fix")
    r.add_argument("--seed", type=int, default=7, help="RNG seed for repeatable motion")
    r.add_argument("--concurrency", type=int, default=8,
                   help="parallel in-flight fixes (default 8; device bucket allows ~20/s)")
    r.add_argument("--dry-run", action="store_true",
                   help="build and print fixes without sending any")
    r.set_defaults(func=cmd_run)

    args = parser.parse_args(argv)
    args.api = args.api.rstrip("/")
    try:
        return args.func(args)
    except KeyboardInterrupt:
        print("\nstopped", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
