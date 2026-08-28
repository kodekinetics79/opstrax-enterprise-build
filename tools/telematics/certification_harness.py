#!/usr/bin/env python3
"""Deterministic staging-only certification traffic for OpsTrax native ingest.

The default mode is a zero-network plan. Execute mode is deliberately difficult to
enable: it requires an exact staging host allowlist, an exact deployed SHA, secure
credential files, and an acknowledgement. It never reads or writes the database.
"""

from __future__ import annotations

import argparse
from concurrent.futures import FIRST_COMPLETED, Future, ThreadPoolExecutor, wait
import csv
from dataclasses import dataclass, replace
from datetime import datetime, timedelta, timezone
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import stat
import sys
import time
from typing import Iterable
from urllib import error, parse, request


INGEST_PATH = "/api/telemetry/ingest"
DIAGNOSTIC_INGEST_PATH = "/api/maintenance/fault-codes/ingest"
EXECUTE_ACK = "I_ACKNOWLEDGE_STAGING_TELEMETRY_MUTATION"
MAX_CREDENTIAL_BYTES = 1_048_576
MAX_DEVICES = 1_100
DEVICES_PER_BRANCH = 220
INSTALLED_PER_BRANCH = 200
OFFLINE_SOAK_SECONDS = 16 * 60
RECONNECT_SECONDS = 17 * 60
# A future-timestamp negative control must remain invalid even when staging
# backpressure stretches a run well beyond its planned schedule.  The API allows
# no more than five minutes of device clock skew; anchor the control one day
# beyond its submission phase instead of only six minutes beyond it.
FUTURE_CONTROL_OFFSET_SECONDS = 24 * 60 * 60
API_FUTURE_SKEW_SECONDS = 5 * 60
MAX_SCENARIOS = 8_000
DEFAULT_RATE_PER_SECOND = 20.0
MAX_RATE_PER_SECOND = 50.0
MAX_EXECUTION_WORKERS = 64
KNOWN_PRODUCTION_HOSTS = {
    "osptrax-fleet-management.onrender.com",
    "opstrax-api.onrender.com",
    "opstrax-enterprise-build-8x41.onrender.com",
}
BRANCH_CENTERS = {
    "CLHQ": (35.2271, -80.8431),
    "NEHUB": (40.7357, -74.1724),
    "SEDEPOT": (32.0809, -81.0912),
    "MWYARD": (43.0389, -87.9065),
    "WESTHUB": (47.6062, -122.3321),
}

# Inclusive device ordinals. These ranges are deliberately identical in every
# branch, making the public oracle easy to reconcile with the customer import.
COHORT_RANGES = (
    ("normal", 1, 140),
    ("delayed", 141, 160),
    ("stale", 161, 175),
    ("offline", 176, 185),
    ("reconnect", 186, 190),
    ("geofence", 191, 195),
    ("odometer", 196, 198),
    ("critical-j1939", 199, 200),
    ("never-connected", 201, 220),
)

EXPECTED_COHORT_TOTALS = {
    "normal": 700,
    "delayed": 100,
    "stale": 75,
    "offline": 50,
    "reconnect": 25,
    "geofence": 25,
    "odometer": 15,
    "critical-j1939": 10,
    "never-connected": 100,
}


@dataclass(frozen=True)
class Credential:
    serial: str
    api_key: str
    hmac_secret: str


@dataclass(frozen=True)
class Scenario:
    name: str
    serial: str
    api_key: str
    hmac_secret: str
    nonce: str
    body: str
    expected_status: tuple[int, ...]
    cohort: str = "control"
    interface: str = "native"
    path: str = INGEST_PATH
    send_offset_seconds: int = 0
    expected_mutation: str = "advance-latest"
    chrome_outcome: str = "Latest trusted position advances"
    timestamp_offset_seconds: int = 0
    signature_mode: str = "valid"

    def public(self) -> dict[str, object]:
        return {
            "name": self.name,
            "deviceSerial": self.serial,
            "cohort": self.cohort,
            "interface": self.interface,
            "path": self.path,
            "sendOffsetSeconds": self.send_offset_seconds,
            "bodySha256": sha256_hex(self.body),
            "expectedStatus": list(self.expected_status),
            "expectedMutation": self.expected_mutation,
            "expectedChromeOutcome": self.chrome_outcome,
        }


def sha256_hex(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def compute_signature(secret: str, method: str, path: str, timestamp: str, nonce: str, body: str) -> str:
    canonical = f"{method}\n{path}\n{timestamp}\n{nonce}\n{sha256_hex(body)}"
    return hmac.new(secret.encode("utf-8"), canonical.encode("utf-8"), hashlib.sha256).hexdigest()


def _secure_regular_file(path: Path) -> None:
    resolved = path.resolve(strict=True)
    info = resolved.stat()
    if not stat.S_ISREG(info.st_mode):
        raise ValueError(f"credential path is not a regular file: {resolved}")
    if info.st_size > MAX_CREDENTIAL_BYTES:
        raise ValueError(f"credential file exceeds {MAX_CREDENTIAL_BYTES} bytes: {resolved}")
    if stat.S_IMODE(info.st_mode) & 0o077:
        raise ValueError(f"credential file must not grant group/world permissions: {resolved}")


def load_credentials(paths: Iterable[str]) -> list[Credential]:
    credentials: list[Credential] = []
    seen_serials: set[str] = set()
    for raw_path in paths:
        path = Path(raw_path)
        _secure_regular_file(path)
        with path.open("r", encoding="utf-8", newline="") as handle:
            reader = csv.DictReader(handle)
            if reader.fieldnames != ["deviceSerial", "apiKey", "hmacSecret"]:
                raise ValueError(f"unexpected credential header in {path}")
            for row_number, row in enumerate(reader, start=2):
                serial = (row.get("deviceSerial") or "").strip()
                api_key = (row.get("apiKey") or "").strip()
                hmac_secret = (row.get("hmacSecret") or "").strip()
                if not serial or not api_key or not hmac_secret:
                    raise ValueError(f"blank credential field in {path} row {row_number}")
                if serial in seen_serials:
                    raise ValueError(f"duplicate device serial across credential files: {serial}")
                seen_serials.add(serial)
                credentials.append(Credential(serial, api_key, hmac_secret))
    if not credentials:
        raise ValueError("no device credentials loaded")
    if len(credentials) > MAX_DEVICES:
        raise ValueError(f"credential set exceeds certification maximum of {MAX_DEVICES}")
    return sorted(credentials, key=lambda value: value.serial)


def _branch_for(serial: str) -> str:
    normalized = serial.upper().replace("-", "")
    for branch in BRANCH_CENTERS:
        if normalized.startswith(branch):
            return branch
    raise ValueError(f"device serial does not map to a certification branch: {serial}")


def _device_number(serial: str) -> int:
    match = re.search(r"(\d+)$", serial)
    if not match:
        raise ValueError(f"device serial has no numeric suffix: {serial}")
    return int(match.group(1))


def _run_tag(run_id: str) -> str:
    return hashlib.sha256(run_id.encode("utf-8")).hexdigest()[:12]


def _json_body(serial: str, observed_at: datetime, run_id: str, *, lat: float | None = None,
               lng: float | None = None, speed: float = 32.0, sequence: int = 0,
               odometer_miles: float | None = None) -> str:
    branch = _branch_for(serial)
    center_lat, center_lng = BRANCH_CENTERS[branch]
    number = _device_number(serial)
    offset = ((number % 25) - 12) * 0.0007
    payload = {
        "accuracyMeters": 8.0,
        "batteryVoltage": 13.8,
        "clientGeneratedId": f"cert-{_run_tag(run_id)}-{serial}-{sequence:06d}",
        "correlationId": f"CERT-LARGE-20260825:{_run_tag(run_id)}:{serial}",
        "engineStatus": "Moving" if speed > 0 else "Idle",
        "eventTime": observed_at.astimezone(timezone.utc).isoformat().replace("+00:00", "Z"),
        "eventType": "position",
        "fuelLevel": round(72.0 - (number % 17) * 0.5, 1),
        "heading": (number * 37 + sequence * 11) % 360,
        "lat": round(center_lat + offset, 6) if lat is None else lat,
        "lng": round(center_lng - offset, 6) if lng is None else lng,
        "odometerMiles": (round(40000 + number * 11.25 + sequence * 0.4, 1)
                          if odometer_miles is None else round(odometer_miles, 1)),
        "sourceChannel": "certification-native-hmac",
        "speedMph": speed,
    }
    return json.dumps(payload, sort_keys=True, separators=(",", ":"))


def _nonce(run_id: str, serial: str, name: str) -> str:
    digest = hashlib.sha256(f"{run_id}:{serial}:{name}".encode("utf-8")).hexdigest()
    return f"cert-{digest[:48]}"


def _cohort(device_ordinal: int) -> str:
    for name, first, last in COHORT_RANGES:
        if first <= device_ordinal <= last:
            return name
    raise ValueError(f"device ordinal must be between 1 and {DEVICES_PER_BRANCH}: {device_ordinal}")


def validate_large_fleet_credentials(credentials: list[Credential]) -> dict[str, list[Credential]]:
    if len(credentials) != MAX_DEVICES:
        raise ValueError(f"large-fleet certification requires exactly {MAX_DEVICES} credentials")
    grouped: dict[str, list[Credential]] = {branch: [] for branch in BRANCH_CENTERS}
    for credential in credentials:
        grouped[_branch_for(credential.serial)].append(credential)
    for branch, rows in grouped.items():
        rows.sort(key=lambda value: (_device_number(value.serial), value.serial))
        if len(rows) != DEVICES_PER_BRANCH:
            raise ValueError(f"branch {branch} requires exactly {DEVICES_PER_BRANCH} credentials")
        ordinals = [_device_number(value.serial) for value in rows]
        if ordinals != list(range(1, DEVICES_PER_BRANCH + 1)):
            raise ValueError(f"branch {branch} device suffixes must be exactly 0001..0220")
    return grouped


def _diagnostic_body(serial: str, observed_at: datetime, run_id: str) -> str:
    payload = {
        "bus": "CAN1",
        "controller": "ECM",
        "description": "Certification critical oil pressure signal",
        "dtcs": [{"fmi": 1, "occurrenceCount": 1, "spn": 100}],
        "lampStatus": {
            "amberWarning": "Off", "amberWarningFlash": "Off",
            "malfunctionIndicator": "Off", "malfunctionIndicatorFlash": "Off",
            "protect": "Off", "protectFlash": "Off",
            "redStop": "On", "redStopFlash": "Off",
        },
        "observedAt": observed_at.astimezone(timezone.utc).isoformat().replace("+00:00", "Z"),
        "pgn": 65226,
        "protocol": "J1939",
        "sourceAddress": 0,
        "sourceEventId": f"CERT-LARGE-20260825:{_run_tag(run_id)}:{serial}:DM1",
    }
    return json.dumps(payload, sort_keys=True, separators=(",", ":"))


def _scenario(credential: Credential, run_id: str, cohort: str, name: str, body: str,
              send_offset: int, *, expected_status: tuple[int, ...] = (200,),
              expected_mutation: str = "advance-latest", chrome_outcome: str,
              interface: str = "native", path: str = INGEST_PATH,
              nonce_name: str | None = None, signature_mode: str = "valid",
              timestamp_offset_seconds: int = 0, api_key: str | None = None) -> Scenario:
    return Scenario(
        name, credential.serial, api_key or credential.api_key, credential.hmac_secret,
        _nonce(run_id, credential.serial, nonce_name or name), body, expected_status,
        cohort=cohort, interface=interface, path=path, send_offset_seconds=send_offset,
        expected_mutation=expected_mutation, chrome_outcome=chrome_outcome,
        timestamp_offset_seconds=timestamp_offset_seconds, signature_mode=signature_mode,
    )


def build_scenarios(credentials: list[Credential], run_id: str, observed_at: datetime) -> list[Scenario]:
    grouped = validate_large_fleet_credentials(credentials)
    scenarios: list[Scenario] = []
    cohort_counts = {name: 0 for name in EXPECTED_COHORT_TOTALS}
    sequence = 0
    for branch in BRANCH_CENTERS:
        for credential in grouped[branch]:
            ordinal = _device_number(credential.serial)
            cohort = _cohort(ordinal)
            cohort_counts[cohort] += 1
            if cohort == "never-connected":
                continue

            def position(name: str, send_offset: int, observed_offset: int | None = None,
                         *, lat: float | None = None, lng: float | None = None,
                         speed: float = 32.0, odometer_miles: float | None = None,
                         mutation: str = "advance-latest", outcome: str) -> None:
                nonlocal sequence
                sequence += 1
                event_offset = send_offset if observed_offset is None else observed_offset
                body = _json_body(credential.serial, observed_at + timedelta(seconds=event_offset), run_id,
                                  lat=lat, lng=lng, speed=speed, sequence=sequence,
                                  odometer_miles=odometer_miles)
                scenarios.append(_scenario(credential, run_id, cohort, name, body, send_offset,
                                           expected_mutation=mutation, chrome_outcome=outcome))

            if cohort == "normal":
                for point, send_offset in enumerate((*range(0, 9 * 60, 60), RECONNECT_SECONDS)):
                    position(f"normal-route-{point + 1:02d}", send_offset,
                             outcome="Online with a fresh in-fence route position")
            elif cohort == "delayed":
                position("delayed-fix-01", 0, -3 * 60,
                         outcome="Online; GPS freshness is Delayed, never Live")
                position("delayed-fix-02", 15 * 60, 12 * 60,
                         outcome="Online; GPS freshness remains Delayed")
            elif cohort == "stale":
                position("stale-fix-01", 0, -20 * 60,
                         outcome="Online check-in with Stale device-fix time")
                position("stale-fix-02", 15 * 60, -5 * 60,
                         outcome="Online check-in remains Stale by device-fix time")
            elif cohort == "offline":
                position("offline-prime", 0, outcome="Initially Online; Offline after the 16-minute no-ingest soak")
            elif cohort == "reconnect":
                position("reconnect-prime", 0, outcome="Initially Online; Offline after the soak")
                position("reconnect-fresh", RECONNECT_SECONDS,
                         outcome="Returns Online/Live; any open stale alert remains until governed resolution")
            elif cohort == "geofence":
                center_lat, center_lng = BRANCH_CENTERS[branch]
                position("geofence-inside-01", 0, outcome="Inside authorized branch geofence; no breach")
                position("geofence-inside-02", 2 * 60, outcome="Inside authorized branch geofence; no breach")
                position("geofence-outside", RECONNECT_SECONDS, lat=round(center_lat + 0.5, 6),
                         lng=round(center_lng + 0.5, 6),
                         outcome="One deduplicated open geofence breach and Needs action")
            elif cohort == "odometer":
                base_odometer = 100_000 + list(BRANCH_CENTERS).index(branch) * 10_000 + ordinal * 10
                for point, send_offset in enumerate((0, 60, 120, 180, RECONNECT_SECONDS)):
                    position(f"odometer-{point + 1:02d}", send_offset,
                             odometer_miles=base_odometer + point * 0.8,
                             outcome="Latest odometer increases; earlier values remain in history")
            elif cohort == "critical-j1939":
                position("critical-gps-01", 2 * 60, outcome="Online with current position")
                position("critical-gps-02", RECONNECT_SECONDS, outcome="Online with current position")
                diagnostic = _diagnostic_body(credential.serial, observed_at + timedelta(seconds=15 * 60), run_id)
                scenarios.append(_scenario(
                    credential, run_id, cohort, "critical-j1939-dm1", diagnostic, 15 * 60,
                    interface="diagnostic-native", path=DIAGNOSTIC_INGEST_PATH,
                    expected_mutation="create-critical-fault-and-hold",
                    chrome_outcome="Diagnostics Issues shows Critical DM1; assigned vehicle is held out of service",
                ))

    if cohort_counts != EXPECTED_COHORT_TOTALS:
        raise AssertionError(f"cohort allocation drifted: {cohort_counts}")

    # Bounded negative controls use one already-installed normal device and never
    # increase inventory. HTTP success can still mean history-only, so the oracle
    # describes projection semantics explicitly rather than equating 200 with mutation.
    primary = grouped[next(iter(BRANCH_CENTERS))][0]
    control_offset = RECONNECT_SECONDS + 5
    replay_body = _json_body(primary.serial, observed_at + timedelta(seconds=control_offset), run_id, sequence=99_001)
    replay_nonce_name = "negative-replay-pair"
    idempotency_body = _json_body(
        primary.serial, observed_at + timedelta(seconds=control_offset + 8), run_id, sequence=99_009)
    idempotency_conflict_payload = json.loads(idempotency_body)
    idempotency_conflict_payload["speedMph"] = 47.0
    idempotency_conflict_body = json.dumps(
        idempotency_conflict_payload, sort_keys=True, separators=(",", ":"))
    scenarios.extend([
        _scenario(primary, run_id, "control", "replay-original", replay_body, control_offset,
                  nonce_name=replay_nonce_name, chrome_outcome="One new history event; latest position advances"),
        _scenario(primary, run_id, "control", "replay-duplicate", replay_body, control_offset,
                  nonce_name=replay_nonce_name, expected_status=(409,), expected_mutation="none",
                  chrome_outcome="No duplicate history, projection, alert, or check-in mutation"),
        _scenario(primary, run_id, "control", "out-of-order-history",
                  _json_body(primary.serial, observed_at + timedelta(seconds=control_offset - 600), run_id, sequence=99_002),
                  control_offset + 1, expected_mutation="history-only",
                  chrome_outcome="Latest position and odometer remain on the newer event"),
        _scenario(primary, run_id, "control", "future-fix",
                  _json_body(
                      primary.serial,
                      observed_at + timedelta(
                          seconds=control_offset + FUTURE_CONTROL_OFFSET_SECONDS),
                      run_id,
                      sequence=99_003,
                  ),
                  control_offset + 2, expected_status=(422,), expected_mutation="none",
                  chrome_outcome="No visible state change"),
        _scenario(primary, run_id, "control", "null-island",
                  _json_body(primary.serial, observed_at, run_id, lat=0, lng=0, sequence=99_004),
                  control_offset + 3, expected_status=(422,), expected_mutation="none",
                  chrome_outcome="No visible state change"),
        _scenario(primary, run_id, "control", "invalid-speed",
                  _json_body(primary.serial, observed_at, run_id, speed=201, sequence=99_005),
                  control_offset + 4, expected_status=(422,), expected_mutation="none",
                  chrome_outcome="No visible state change"),
        _scenario(primary, run_id, "control", "bad-signature",
                  _json_body(primary.serial, observed_at, run_id, sequence=99_006), control_offset + 5,
                  expected_status=(401,), expected_mutation="none", chrome_outcome="No visible state change",
                  signature_mode="invalid"),
        _scenario(primary, run_id, "control", "stale-transport-timestamp",
                  _json_body(primary.serial, observed_at, run_id, sequence=99_007), control_offset + 6,
                  expected_status=(422,), expected_mutation="none", chrome_outcome="No visible state change",
                  timestamp_offset_seconds=-120),
        _scenario(primary, run_id, "control", "unrecognized-device-key",
                  _json_body(primary.serial, observed_at, run_id, sequence=99_008), control_offset + 7,
                  expected_status=(401,), expected_mutation="none", chrome_outcome="No visible state change",
                  api_key=f"invalid-{primary.api_key}"),
        _scenario(primary, run_id, "control", "idempotency-original",
                  idempotency_body, control_offset + 8,
                  chrome_outcome="One new history event; latest position advances"),
        _scenario(primary, run_id, "control", "idempotency-identical-fresh-nonce",
                  idempotency_body, control_offset + 9, expected_mutation="none",
                  chrome_outcome="HTTP success identifies replay; no duplicate history or projection mutation"),
        _scenario(primary, run_id, "control", "idempotency-conflict-fresh-nonce",
                  idempotency_conflict_body, control_offset + 10, expected_status=(409,),
                  expected_mutation="none",
                  chrome_outcome="Altered payload with reused ClientGeneratedId is rejected with no mutation"),
    ])
    # Stable sort retains deliberate same-device order (notably replay original
    # before replay duplicate) while globally respecting the time-phased schedule.
    scenarios.sort(key=lambda value: (value.send_offset_seconds, value.serial))
    if len(scenarios) > MAX_SCENARIOS:
        raise AssertionError(f"scenario count exceeds safe cap of {MAX_SCENARIOS}")
    return scenarios


def build_public_plan(credentials: list[Credential], scenarios: list[Scenario], run_id: str,
                      host: str, execute: bool) -> dict[str, object]:
    cohort_counts = {name: 0 for name in EXPECTED_COHORT_TOTALS}
    for credential in credentials:
        cohort_counts[_cohort(_device_number(credential.serial))] += 1
    interface_counts: dict[str, int] = {}
    for scenario in scenarios:
        interface_counts[scenario.interface] = interface_counts.get(scenario.interface, 0) + 1
    return {
        "schemaVersion": 1,
        "mode": "execute" if execute else "plan",
        "networkCalls": None if execute else 0,
        "targetHost": host,
        "runId": run_id,
        "credentialCount": len(credentials),
        "branchCount": len(BRANCH_CENTERS),
        "devicesPerBranch": DEVICES_PER_BRANCH,
        "installedPerBranch": INSTALLED_PER_BRANCH,
        "cohortPerBranch": {
            name: last - first + 1 for name, first, last in COHORT_RANGES
        },
        "expectedInventory": {"devices": 1100, "installed": 1000, "neverConnected": 100},
        "cohortTotals": cohort_counts,
        "interfaceEventTotals": interface_counts,
        "eventTotals": {
            "cohortGps": 7620,
            "positiveControlGps": 3,
            "idempotentNoOpSuccess": 1,
            "validDiagnostics": 10,
            "rejectedControls": 8,
            "negativeOrNoMutationControls": 9,
            "allPlannedAttempts": len(scenarios),
        },
        "scenarioCount": len(scenarios),
        "phaseOffsetsSeconds": {"offlineObservation": OFFLINE_SOAK_SECONDS, "reconnect": RECONNECT_SECONDS},
        "expectedChromeTotals": {
            "managed": 1100, "onlineAfterReconnect": 950, "neverConnected": 100,
            "offlineOrNeverConnected": 150, "faultedForAuthorizedRole": 10,
            "gpsLive": 775, "gpsDelayed": 100, "gpsStale": 125, "gpsNoPosition": 100,
            "gpsOfflineOrNoPositionFilter": 225,
            "needsActionBeforeReconnectAlertResolution": 210,
            "needsActionAfterReconnectAlertResolution": 185,
        },
        "governedUiPreconditions": [
            "Import all 1,100 devices and capture one-time credentials without recording secrets in public evidence.",
            "Install and commission device ordinals 0001..0200 in every branch against that branch's 200 vehicles.",
            "Leave ordinals 0201..0220 uninstalled and never submit telemetry for them.",
            "Create one 25-km authorized-area geofence around each published branch center.",
        ],
        "externalControls": [{
            "name": "wrong-tenant-gateway",
            "requiredInterface": "/api/telemetry/gps-ingest",
            "expectedStatus": 403,
            "expectedMutation": "none",
            "precondition": "separately governed isolated negative-control tenant/device and tenant-bound gateway",
        }],
        "scenarios": [scenario.public() for scenario in scenarios],
    }


def validate_target(base_url: str, allow_host: str, environment: str) -> tuple[str, str]:
    parsed = parse.urlparse(base_url)
    host = (parsed.hostname or "").lower()
    if environment != "staging":
        raise ValueError("certification harness executes only against staging")
    if parsed.scheme != "https" or not host or parsed.path not in ("", "/"):
        raise ValueError("base URL must be an origin-only HTTPS URL")
    if host in KNOWN_PRODUCTION_HOSTS:
        raise ValueError("certification harness refuses a known production host")
    if host != allow_host.lower().strip():
        raise ValueError("target host must exactly match --allow-host")
    return base_url.rstrip("/"), host


def _open_json(req: request.Request, timeout: float) -> tuple[int, dict[str, str], bytes, float]:
    started = time.perf_counter()
    try:
        with request.urlopen(req, timeout=timeout) as response:
            return response.status, dict(response.headers.items()), response.read(), time.perf_counter() - started
    except error.HTTPError as exc:
        return exc.code, dict(exc.headers.items()), exc.read(), time.perf_counter() - started


def preflight(base_url: str, expected_sha: str, timeout: float) -> dict[str, object]:
    req = request.Request(f"{base_url}/health/ready", method="GET", headers={"Accept": "application/json"})
    status_code, headers, body, elapsed = _open_json(req, timeout)
    if status_code != 200:
        raise RuntimeError(f"readiness returned HTTP {status_code}")
    try:
        payload = json.loads(body)
    except json.JSONDecodeError as exc:
        raise RuntimeError("readiness did not return JSON") from exc
    version = str(payload.get("version") or headers.get("x-deployment-version") or "")
    if version != expected_sha:
        raise RuntimeError(f"readiness SHA mismatch: expected {expected_sha}, observed {version or 'missing'}")
    environment = str(payload.get("environment") or "")
    if environment != "Staging":
        raise RuntimeError(
            f"readiness environment mismatch: expected Staging, observed {environment or 'missing'}")
    return {
        "status": status_code,
        "version": version,
        "environment": environment,
        "elapsedMs": round(elapsed * 1000, 1),
    }


def _validate_scenario_time_oracle(
    scenario: Scenario, now: datetime | None = None,
) -> None:
    """Fail closed if a time-based negative control has aged into validity."""
    if scenario.name != "future-fix":
        return
    try:
        raw_event_time = json.loads(scenario.body)["eventTime"]
        event_time = datetime.fromisoformat(str(raw_event_time).replace("Z", "+00:00"))
    except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
        raise RuntimeError("future-fix control has no valid eventTime") from exc
    current = now or datetime.now(timezone.utc)
    if (event_time - current).total_seconds() <= API_FUTURE_SKEW_SECONDS:
        raise RuntimeError(
            "future-fix control aged into the API acceptance window; refusing to send"
        )


def execute_scenario(base_url: str, scenario: Scenario, expected_sha: str, timeout: float) -> dict[str, object]:
    _validate_scenario_time_oracle(scenario)
    timestamp = str(int(time.time()) + scenario.timestamp_offset_seconds)
    signature = compute_signature(
        scenario.hmac_secret, "POST", scenario.path, timestamp, scenario.nonce, scenario.body,
    )
    if scenario.signature_mode == "invalid":
        signature = ("0" if signature[0] != "0" else "1") + signature[1:]
    req = request.Request(
        f"{base_url}{scenario.path}", data=scenario.body.encode("utf-8"), method="POST",
        headers={
            "Accept": "application/json",
            "Content-Type": "application/json",
            "X-Device-Key": scenario.api_key,
            "X-Timestamp": timestamp,
            "X-Nonce": scenario.nonce,
            "X-Signature": signature,
        },
    )
    status_code, headers, body, elapsed = _open_json(req, timeout)
    deployment_sha = headers.get("x-deployment-version", "")
    if deployment_sha and deployment_sha != expected_sha:
        raise RuntimeError(f"response SHA mismatch: expected {expected_sha}, observed {deployment_sha}")
    return {
        "name": scenario.name,
        "deviceSerial": scenario.serial,
        "cohort": scenario.cohort,
        "interface": scenario.interface,
        "status": status_code,
        "expectedStatus": list(scenario.expected_status),
        "passed": status_code in scenario.expected_status,
        "elapsedMs": round(elapsed * 1000, 1),
        "responseSha256": hashlib.sha256(body).hexdigest(),
        "deploymentSha": deployment_sha or None,
    }


def _wait_for_submission_slot(due: float, next_rate_slot: float) -> float:
    """Wait for the later of the phase due time, rate slot, and current time.

    Including the current time deliberately discards pacing debt after endpoint
    backpressure. A delayed run therefore extends instead of submitting a burst
    to catch up with an obsolete schedule.
    """
    now = time.monotonic()
    target = max(due, next_rate_slot, now)
    delay = target - now
    if delay > 0:
        time.sleep(delay)
    return time.monotonic()


def execute_scenarios(
    base_url: str,
    scenarios: list[Scenario],
    expected_sha: str,
    timeout: float,
    rate_per_second: float,
) -> tuple[int, int]:
    """Execute at the planned submission rate without serializing on response latency.

    The rate remains a hard submission ceiling. Bounded workers and pending futures
    prevent an unhealthy endpoint from creating an unbounded local queue.
    """
    worker_count = min(MAX_EXECUTION_WORKERS, max(4, int(rate_per_second * 4)))
    pending_limit = worker_count * 2
    execution_started = time.monotonic()
    next_rate_slot = execution_started
    completed = 0
    failed = 0
    pending: dict[Future[dict[str, object]], Scenario] = {}
    inflight_by_serial: dict[str, Future[dict[str, object]]] = {}

    def record(done: set[Future[dict[str, object]]]) -> None:
        nonlocal completed, failed
        for future in done:
            scenario = pending.pop(future)
            if inflight_by_serial.get(scenario.serial) is future:
                del inflight_by_serial[scenario.serial]
            try:
                result = future.result()
            except (OSError, RuntimeError) as exc:
                result = {
                    "name": scenario.name,
                    "deviceSerial": scenario.serial,
                    "cohort": scenario.cohort,
                    "interface": scenario.interface,
                    "passed": False,
                    "error": str(exc),
                }
            print(json.dumps(result, sort_keys=True), flush=True)
            completed += 1
            failed += 0 if result.get("passed") else 1

    with ThreadPoolExecutor(max_workers=worker_count, thread_name_prefix="cert-telemetry") as executor:
        for scenario in scenarios:
            # Each device's events form an ordered certification story. In
            # particular, replay-original must commit before replay-duplicate.
            # Wait in the scheduler rather than occupying a worker with a
            # predecessor wait, which could starve the bounded pool.
            previous = inflight_by_serial.get(scenario.serial)
            if previous is not None and previous in pending:
                done, _ = wait({previous}, return_when=FIRST_COMPLETED)
                record(done)

            due = execution_started + scenario.send_offset_seconds
            _wait_for_submission_slot(due, next_rate_slot)
            future = executor.submit(execute_scenario, base_url, scenario, expected_sha, timeout)
            pending[future] = scenario
            inflight_by_serial[scenario.serial] = future
            next_rate_slot = time.monotonic() + (1.0 / rate_per_second)
            if len(pending) >= pending_limit:
                done, _ = wait(pending, return_when=FIRST_COMPLETED)
                record(done)

        while pending:
            done, _ = wait(pending, return_when=FIRST_COMPLETED)
            record(done)

    return completed, failed


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--credentials", action="append", required=True, help="mode-0600 one-time credential CSV; repeatable")
    parser.add_argument("--run-id", required=True, help="unique certification run identifier")
    parser.add_argument("--observed-at", help="ISO-8601 scenario anchor; defaults to current UTC")
    parser.add_argument("--base-url", default="https://opstrax-staging-api.onrender.com")
    parser.add_argument("--allow-host", default="opstrax-staging-api.onrender.com")
    parser.add_argument("--environment", default="staging")
    parser.add_argument("--expected-sha", help="required 40-character SHA for execute mode")
    parser.add_argument("--timeout", type=float, default=30.0)
    parser.add_argument("--rate-per-second", type=float, default=DEFAULT_RATE_PER_SECOND,
                        help=f"execution pacing; positive and no more than {MAX_RATE_PER_SECOND:g}")
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--execute-ack", default="")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    try:
        base_url, host = validate_target(args.base_url, args.allow_host, args.environment)
        credentials = load_credentials(args.credentials)
        validate_large_fleet_credentials(credentials)
        if args.timeout <= 0 or args.timeout > 60:
            raise ValueError("--timeout must be greater than 0 and no more than 60 seconds")
        if args.rate_per_second <= 0 or args.rate_per_second > MAX_RATE_PER_SECOND:
            raise ValueError(f"--rate-per-second must be greater than 0 and no more than {MAX_RATE_PER_SECOND:g}")
        observed_at = (datetime.fromisoformat(args.observed_at.replace("Z", "+00:00"))
                       if args.observed_at else datetime.now(timezone.utc).replace(microsecond=0))
        if observed_at.tzinfo is None:
            raise ValueError("--observed-at must include a timezone")
        scenarios = build_scenarios(credentials, args.run_id, observed_at)
        public_plan = build_public_plan(credentials, scenarios, args.run_id, host, args.execute)
        if not args.execute:
            print(json.dumps(public_plan, indent=2, sort_keys=True))
            return 0
        if args.execute_ack != EXECUTE_ACK:
            raise ValueError(f"execute mode requires --execute-ack {EXECUTE_ACK}")
        if not args.expected_sha or not re.fullmatch(r"[0-9a-f]{40}", args.expected_sha):
            raise ValueError("execute mode requires --expected-sha as 40 lowercase hexadecimal characters")
        if abs((datetime.now(timezone.utc) - observed_at.astimezone(timezone.utc)).total_seconds()) > 60:
            raise ValueError("execute mode requires --observed-at within 60 seconds of current UTC")

        print(json.dumps({"preflight": preflight(base_url, args.expected_sha, args.timeout)}, sort_keys=True), flush=True)
        completed, failed = execute_scenarios(
            base_url, scenarios, args.expected_sha, args.timeout, args.rate_per_second)
        print(json.dumps({"completed": completed, "failed": failed, "runId": args.run_id}, sort_keys=True), flush=True)
        return 0 if failed == 0 else 1
    except (OSError, ValueError, RuntimeError) as exc:
        print(f"certification harness refused: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
