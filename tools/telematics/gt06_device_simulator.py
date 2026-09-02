#!/usr/bin/env python3
"""Simulate one or many GT06 trackers against an OpsTrax device edge.

The other tools in this directory observe a device: `capture_listener.py` records what one sends,
`fingerprint.py` reads a capture offline, `public_replay.py` sends exactly one committed fixture.
None of them BEHAVES like a device. That gap is why the whole GT06 remediation could only be
validated by unit tests and by waiting for hardware.

This one is the device. It opens a socket, logs in with a packed-BCD IMEI, streams location,
heartbeat and alarm frames with real CRC-ITU checksums, reads the server's acknowledgements back,
and can act out the scenarios that actually break gateways: a power cycle that restarts the frame
counter, a reconnect while the old socket is still open, a second login claiming a different
device, a corrupted checksum between good frames, a frame dribbled a byte at a time.

WHAT IT IS NOT. It is not a substitute for a physical bench. It speaks the protocol as this
repository understands it, so it cannot discover that our reading of the protocol is wrong — the
exact failure mode the hemisphere defect had. Its value is in rehearsing everything else, so the
one real tracker is spent on questions only hardware can answer.

Safety, matching the conventions of the other tools here:
  * `--environment` is required; `production` is refused outright.
  * A non-loopback target additionally requires `--allow-host` naming it exactly, plus the
    acknowledgement string, so a fat-fingered address cannot reach a real fleet edge.
  * Known production hosts are refused even if allow-listed.
  * The default is a zero-network plan: nothing is sent without `--send`.
  * Every identity is synthetic and generated from the `--imei-base`; no real IMEI is embedded.

Examples
--------
    # Show what would be sent, touching no network at all.
    python3 tools/telematics/gt06_device_simulator.py --scenario drive --devices 3

    # Drive a locally running gateway.
    python3 tools/telematics/gt06_device_simulator.py \
        --scenario drive --devices 10 --host 127.0.0.1 --port 5023 \
        --environment local --send

    # Rehearse the power-cycle serial reset the replay epoch exists for.
    python3 tools/telematics/gt06_device_simulator.py \
        --scenario power-cycle --host 127.0.0.1 --port 5023 --environment local --send

    python3 tools/telematics/gt06_device_simulator.py --self-test
"""

from __future__ import annotations

import argparse
import datetime as _dt
import random
import socket
import sys
import threading
import time
from dataclasses import dataclass, field

# ── Protocol ──────────────────────────────────────────────────────────────────
# Frame: start(2) | length(1) | protocol(1) | information(N) | serial(2) | crc(2) | stop(2)
# length counts protocol + information + serial + crc. CRC-ITU over [length .. serial].

START = b"\x78\x78"
STOP = b"\x0d\x0a"

PROTO_LOGIN = 0x01
PROTO_LOCATION = 0x12
PROTO_STATUS = 0x13
PROTO_ALARM = 0x16
PROTO_TIME = 0x8A

KNOWN_PRODUCTION_HOSTS = frozenset({
    "opstrax.vercel.app",
    "osptrax-fleet-management.onrender.com",
    "opstrax-enterprise-build.onrender.com",
})
SEND_ACK = "I_UNDERSTAND_THIS_SENDS_SYNTHETIC_DEVICE_TRAFFIC"

# Course/status bit table, from the GT06 vendor document. BYTE1 bit N is word bit N+8.
BIT_NORTH = 1 << 10          # 1 = North latitude, 0 = South
BIT_WEST = 1 << 11           # 1 = West longitude, 0 = East
BIT_POSITIONED = 1 << 12
BIT_DIFFERENTIAL = 1 << 13   # 0 = real-time GPS, 1 = differential


def crc16_itu(data: bytes) -> int:
    """CRC-ITU / CRC-16/X.25: reflected poly 0x8408, init 0xFFFF, xorout 0xFFFF."""
    crc = 0xFFFF
    for byte in data:
        crc ^= byte
        for _ in range(8):
            crc = (crc >> 1) ^ 0x8408 if crc & 1 else crc >> 1
    return ~crc & 0xFFFF


def build_frame(protocol: int, content: bytes, serial: int) -> bytes:
    """Assembles one 0x7878 frame with a correct checksum."""
    if not 0 <= serial <= 0xFFFF:
        raise ValueError("serial must fit in 16 bits")
    packet_length = 1 + len(content) + 2 + 2
    if packet_length > 0xFF:
        raise ValueError("content too long for single-byte length framing")
    region = bytes([packet_length, protocol]) + content + serial.to_bytes(2, "big")
    return START + region + crc16_itu(region).to_bytes(2, "big") + STOP


def pack_imei(imei: str) -> bytes:
    """Packs a 15-digit IMEI into the 8-byte BCD terminal id, one leading pad nibble."""
    if not imei.isdigit() or len(imei) > 16:
        raise ValueError("imei must be up to 16 decimal digits")
    padded = imei.rjust(16, "0")
    return bytes(((int(padded[i * 2]) << 4) | int(padded[i * 2 + 1])) for i in range(8))


def login_frame(imei: str, serial: int = 1) -> bytes:
    return build_frame(PROTO_LOGIN, pack_imei(imei), serial)


def location_frame(serial: int, when: _dt.datetime, lat: float, lng: float,
                   speed_kph: int = 60, course: int = 90, differential: bool = False) -> bytes:
    """A 0x12 GPS frame. Sign lives in the status bits, magnitude in the coordinate."""
    status = (course & 0x03FF) | BIT_POSITIONED
    if lat >= 0:
        status |= BIT_NORTH
    if lng < 0:
        status |= BIT_WEST
    if differential:
        status |= BIT_DIFFERENTIAL

    content = bytes([when.year - 2000, when.month, when.day, when.hour, when.minute, when.second, 0x09])
    content += int(round(abs(lat) * 1_800_000)).to_bytes(4, "big")
    content += int(round(abs(lng) * 1_800_000)).to_bytes(4, "big")
    content += bytes([min(speed_kph, 255)])
    content += status.to_bytes(2, "big")
    content += bytes.fromhex("01CC0101550009C6")   # trailing LBS block
    return build_frame(PROTO_LOCATION, content, serial)


def heartbeat_frame(serial: int, ignition: bool = True, charging: bool = True,
                    oil_connected: bool = True, voltage: int = 5, gsm: int = 4,
                    alarm: int = 0x00) -> bytes:
    """A 0x13 status frame. Terminal-info bit 7 is set when oil/electricity is DISCONNECTED."""
    terminal = 0x40                              # bit6 GPS tracking on
    terminal |= 0x02 if ignition else 0
    terminal |= 0x04 if charging else 0
    terminal |= 0x00 if oil_connected else 0x80
    return build_frame(PROTO_STATUS, bytes([terminal, voltage, gsm, alarm, 0x02]), serial)


def alarm_frame(serial: int, when: _dt.datetime, lat: float, lng: float, alarm_code: int) -> bytes:
    """A 0x16 alarm: the GPS block, then LBS, then the five-byte status tail."""
    body = location_frame(serial, when, lat, lng)
    content = body[4:-6]                          # strip framing, serial and checksum
    content += bytes([0x46, 0x05, 0x04, alarm_code, 0x02])
    return build_frame(PROTO_ALARM, content, serial)


def time_request_frame(serial: int) -> bytes:
    return build_frame(PROTO_TIME, b"", serial)


def corrupt_checksum(frame: bytes) -> bytes:
    """Flips one checksum byte, leaving framing and stop bits intact."""
    corrupted = bytearray(frame)
    corrupted[-3] ^= 0xFF
    return bytes(corrupted)


def parse_ack(data: bytes) -> dict | None:
    """Reads a server response frame, verifying its checksum independently."""
    if len(data) < 10 or data[0:2] != START or data[-2:] != STOP:
        return None
    packet_length = data[2]
    crc_at = 3 + packet_length - 2
    if crc_at + 2 > len(data):
        return None
    wire = int.from_bytes(data[crc_at:crc_at + 2], "big")
    calc = crc16_itu(data[2:crc_at])
    return {
        "protocol": data[3],
        "serial": int.from_bytes(data[crc_at - 2:crc_at], "big"),
        "crc_ok": wire == calc,
        "bytes": len(data),
    }


# ── One simulated device ──────────────────────────────────────────────────────

@dataclass
class DeviceResult:
    imei: str
    frames_sent: int = 0
    acks_received: int = 0
    acks_bad_crc: int = 0
    closed_by_server: bool = False
    errors: list[str] = field(default_factory=list)


class SimulatedDevice:
    """One tracker on one socket. Deliberately blocking and simple: a real tracker is too."""

    def __init__(self, imei: str, host: str, port: int, timeout: float = 10.0):
        self.imei = imei
        self.host = host
        self.port = port
        self.timeout = timeout
        self.serial = 1
        self.sock: socket.socket | None = None
        self.result = DeviceResult(imei=imei)

    # -- connection ------------------------------------------------------------
    def connect(self) -> None:
        self.sock = socket.create_connection((self.host, self.port), timeout=self.timeout)
        self.sock.settimeout(self.timeout)

    def close(self, reset: bool = False) -> None:
        if self.sock is None:
            return
        try:
            if reset:
                # SO_LINGER 0 makes close() send RST instead of FIN.
                self.sock.setsockopt(socket.SOL_SOCKET, socket.SO_LINGER,
                                     (1).to_bytes(2, "little") + (0).to_bytes(6, "little"))
            self.sock.close()
        except OSError:
            pass
        finally:
            self.sock = None

    def power_cycle(self) -> None:
        """What a real tracker does on ignition-off/on: drop the socket AND restart the counter."""
        self.close()
        self.serial = 1

    # -- traffic ---------------------------------------------------------------
    def send(self, frame: bytes, chunked: bool = False) -> None:
        if self.sock is None:
            raise RuntimeError("device is not connected")
        if chunked:
            for byte in frame:                    # one byte at a time, worst-case fragmentation
                self.sock.sendall(bytes([byte]))
                time.sleep(0.001)
        else:
            self.sock.sendall(frame)
        self.result.frames_sent += 1

    def read_ack(self, expect: bool = True) -> dict | None:
        if self.sock is None:
            return None
        try:
            data = self.sock.recv(256)
        except socket.timeout:
            if expect:
                self.result.errors.append("timed out waiting for an acknowledgement")
            return None
        if not data:
            self.result.closed_by_server = True
            return None
        ack = parse_ack(data)
        if ack is None:
            self.result.errors.append(f"unparseable server reply: {data.hex()}")
            return None
        self.result.acks_received += 1
        if not ack["crc_ok"]:
            self.result.acks_bad_crc += 1
            self.result.errors.append("server acknowledgement failed its own checksum")
        return ack

    def next_serial(self) -> int:
        current = self.serial
        self.serial = 1 if current >= 0xFFFF else current + 1
        return current

    def login(self, imei: str | None = None) -> dict | None:
        self.send(login_frame(imei or self.imei, self.next_serial()))
        return self.read_ack()

    def send_location(self, when: _dt.datetime, lat: float, lng: float, **kw) -> None:
        self.send(location_frame(self.next_serial(), when, lat, lng, **kw))

    def send_heartbeat(self, **kw) -> dict | None:
        self.send(heartbeat_frame(self.next_serial(), **kw))
        return self.read_ack()

    def send_alarm(self, when: _dt.datetime, lat: float, lng: float, code: int) -> dict | None:
        self.send(alarm_frame(self.next_serial(), when, lat, lng, code))
        return self.read_ack()

    def server_closed(self, wait: float = 2.0) -> bool:
        """True when the server hangs up within the window."""
        if self.sock is None:
            return True
        self.sock.settimeout(wait)
        try:
            closed = self.sock.recv(64) == b""
        except socket.timeout:
            closed = False
        except OSError:
            closed = True
        finally:
            if self.sock is not None:
                self.sock.settimeout(self.timeout)
        self.result.closed_by_server = self.result.closed_by_server or closed
        return closed


# ── Scenarios ─────────────────────────────────────────────────────────────────

def _route(index: int, step: int) -> tuple[float, float]:
    """A short northbound drive. Northern/western hemisphere, like the seeded fleet."""
    return 38.8951 + (index * 0.01) + (step * 0.002), -77.0364 - (step * 0.001)


def scenario_drive(devices: list[SimulatedDevice], steps: int, log) -> None:
    """The ordinary case: log in, drive, beat, alarm once."""
    now = _dt.datetime.now(_dt.timezone.utc).replace(tzinfo=None, microsecond=0)
    for index, dev in enumerate(devices):
        dev.connect()
        ack = dev.login()
        log(f"  {dev.imei}  login ack={_fmt(ack)}")
    for step in range(steps):
        for index, dev in enumerate(devices):
            lat, lng = _route(index, step)
            dev.send_location(now + _dt.timedelta(seconds=step * 30), lat, lng, speed_kph=55 + step)
    for dev in devices:
        log(f"  {dev.imei}  heartbeat ack={_fmt(dev.send_heartbeat())}")
    lat, lng = _route(0, steps)
    log(f"  {devices[0].imei}  SOS alarm ack={_fmt(devices[0].send_alarm(now, lat, lng, 0x01))}")


def scenario_power_cycle(devices: list[SimulatedDevice], steps: int, log) -> None:
    """Ignition off then on: the socket drops and the frame counter restarts at 1."""
    now = _dt.datetime.now(_dt.timezone.utc).replace(tzinfo=None, microsecond=0)
    dev = devices[0]
    dev.connect()
    log(f"  login ack={_fmt(dev.login())}")
    for step in range(steps):
        dev.send_location(now + _dt.timedelta(seconds=step * 30), *_route(0, step))
    log(f"  sent {steps} fixes, counter now at {dev.serial}")

    dev.power_cycle()
    log("  --- power cycle: socket dropped, counter reset to 1 ---")
    dev.connect()
    log(f"  re-login ack={_fmt(dev.login())}  (serial restarted at 1)")
    for step in range(steps):
        dev.send_location(now + _dt.timedelta(minutes=5 + step), *_route(0, step))
    log(f"  sent {steps} post-reboot fixes at serials 2..{dev.serial - 1}")


def scenario_identity_change(devices: list[SimulatedDevice], steps: int, log) -> None:
    """The attack: a bound socket tries to re-identify as another device."""
    now = _dt.datetime.now(_dt.timezone.utc).replace(tzinfo=None, microsecond=0)
    dev, other = devices[0], devices[1]
    dev.connect()
    log(f"  login as {dev.imei}  ack={_fmt(dev.login())}")
    dev.send_location(now, *_route(0, 0))
    log(f"  sent one fix as {dev.imei}")
    dev.send(login_frame(other.imei, dev.next_serial()))
    log(f"  sent a SECOND login claiming {other.imei} on the same socket")
    ack = dev.read_ack(expect=False)
    log(f"  server reply to the second login: {_fmt(ack) if ack else 'NONE (correct)'}")
    log(f"  server closed the connection: {dev.server_closed()}  (expected True)")


def scenario_duplicate_imei(devices: list[SimulatedDevice], steps: int, log) -> None:
    """Two sockets, one identity: the reconnect-before-timeout case."""
    first, second = devices[0], SimulatedDevice(devices[0].imei, devices[0].host, devices[0].port)
    first.connect()
    log(f"  socket A login ack={_fmt(first.login())}")
    second.connect()
    log(f"  socket B login ack={_fmt(second.login())}  (same IMEI)")
    log(f"  socket A closed by server: {first.server_closed()}  (latest-wins expects True)")
    log(f"  socket B still open:       {not second.server_closed(wait=1.0)}")
    second.close()


def scenario_bad_crc(devices: list[SimulatedDevice], steps: int, log) -> None:
    """A corrupt frame between two good ones must not cost the connection."""
    now = _dt.datetime.now(_dt.timezone.utc).replace(tzinfo=None, microsecond=0)
    dev = devices[0]
    dev.connect()
    log(f"  login ack={_fmt(dev.login())}")
    dev.send(location_frame(dev.next_serial(), now, *_route(0, 0)))
    dev.send(corrupt_checksum(location_frame(dev.next_serial(), now, *_route(0, 1))))
    dev.send(location_frame(dev.next_serial(), now, *_route(0, 2)))
    log("  sent good / CORRUPT / good")
    log(f"  server closed the connection: {dev.server_closed()}  (expected False)")


def scenario_fragmented(devices: list[SimulatedDevice], steps: int, log) -> None:
    """Worst-case TCP fragmentation: one frame delivered a byte at a time."""
    now = _dt.datetime.now(_dt.timezone.utc).replace(tzinfo=None, microsecond=0)
    dev = devices[0]
    dev.connect()
    log(f"  login ack={_fmt(dev.login())}")
    dev.send(heartbeat_frame(dev.next_serial()), chunked=True)
    log(f"  heartbeat sent one byte at a time, ack={_fmt(dev.read_ack())}")
    a = location_frame(dev.next_serial(), now, *_route(0, 0))
    b = location_frame(dev.next_serial(), now, *_route(0, 1))
    dev.sock.sendall(a + b[:6])
    time.sleep(0.15)
    dev.sock.sendall(b[6:])
    dev.result.frames_sent += 2
    log("  sent frame A plus half of B, then the remainder")
    log(f"  server closed the connection: {dev.server_closed()}  (expected False)")


def scenario_reconnect_soak(devices: list[SimulatedDevice], steps: int, log) -> None:
    """Connect/disconnect churn, alternating graceful FIN and abortive RST."""
    dev = devices[0]
    for cycle in range(max(steps, 10)):
        dev.connect()
        dev.login()
        dev.close(reset=bool(cycle % 2))
    log(f"  completed {max(steps, 10)} connect/login/disconnect cycles (alternating FIN and RST)")


SCENARIOS = {
    "drive": scenario_drive,
    "power-cycle": scenario_power_cycle,
    "identity-change": scenario_identity_change,
    "duplicate-imei": scenario_duplicate_imei,
    "bad-crc": scenario_bad_crc,
    "fragmented": scenario_fragmented,
    "reconnect-soak": scenario_reconnect_soak,
}

MIN_DEVICES = {"identity-change": 2, "duplicate-imei": 1}


def _fmt(ack: dict | None) -> str:
    if ack is None:
        return "none"
    return f"proto=0x{ack['protocol']:02X} serial={ack['serial']} crc={'ok' if ack['crc_ok'] else 'BAD'}"


# ── Safety ────────────────────────────────────────────────────────────────────

def validate_target(host: str, port: int, environment: str, allowed: set[str],
                    acknowledgement: str | None, sending: bool) -> None:
    normalized = host.strip().lower().rstrip(".")
    if environment == "production":
        raise ValueError("the simulator refuses environment=production")
    # Colons are rejected so a "host:port" string cannot be mistaken for a hostname, but that
    # would also exclude the IPv6 loopback literal, and the gateway can bind ::. Allow exactly
    # the loopback literal and nothing else with a colon in it.
    if normalized != "::1" and (not normalized or any(c in normalized for c in "/:@")):
        raise ValueError("host must be a bare hostname or IP address")
    if not 1 <= port <= 65535:
        raise ValueError("port must be between 1 and 65535")
    if normalized in KNOWN_PRODUCTION_HOSTS:
        raise ValueError("the simulator refuses a known production host")
    if not sending:
        return
    loopback = normalized in {"127.0.0.1", "::1", "localhost"}
    if loopback:
        return
    if normalized not in allowed:
        raise ValueError("a non-loopback target must be named exactly by --allow-host")
    if acknowledgement != SEND_ACK:
        raise ValueError(f"a non-loopback target requires --acknowledge {SEND_ACK}")


def synthetic_imeis(base: str, count: int) -> list[str]:
    if not base.isdigit() or len(base) != 15:
        raise ValueError("imei-base must be exactly 15 digits")
    prefix = base[:-5]
    start = int(base[-5:])
    return [f"{prefix}{(start + i) % 100000:05d}" for i in range(count)]


# ── Entry point ───────────────────────────────────────────────────────────────

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Simulate GT06 trackers against an OpsTrax device edge.")
    parser.add_argument("--scenario", choices=sorted(SCENARIOS), default="drive")
    parser.add_argument("--devices", type=int, default=1)
    parser.add_argument("--steps", type=int, default=5, help="fixes per device, or cycles for the soak")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5023)
    parser.add_argument("--environment", choices=("local", "staging", "production"), default="local")
    parser.add_argument("--allow-host", action="append", default=[])
    parser.add_argument("--acknowledge", default=None)
    parser.add_argument("--imei-base", default="868120300000000", help="synthetic 15-digit base")
    parser.add_argument("--timeout", type=float, default=10.0)
    parser.add_argument("--send", action="store_true", help="actually open sockets; default is a plan only")
    parser.add_argument("--self-test", action="store_true", help="offline protocol self-check")
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()

    if args.devices < MIN_DEVICES.get(args.scenario, 1):
        parser.error(f"scenario '{args.scenario}' needs at least {MIN_DEVICES[args.scenario]} devices")
    if args.devices < 1:
        parser.error("--devices must be positive")

    try:
        validate_target(args.host, args.port, args.environment,
                        set(h.strip().lower() for h in args.allow_host),
                        args.acknowledge, args.send)
        imeis = synthetic_imeis(args.imei_base, args.devices)
    except ValueError as exc:
        print(f"refused: {exc}", file=sys.stderr)
        return 2

    print(f"scenario    : {args.scenario}")
    print(f"target      : {args.host}:{args.port} ({args.environment})")
    print(f"devices     : {args.devices}  synthetic IMEIs {imeis[0]}..{imeis[-1]}")
    print(f"steps       : {args.steps}")

    if not args.send:
        print("\nPLAN ONLY — no socket was opened. Re-run with --send to execute.")
        sample = login_frame(imeis[0])
        print(f"  sample login frame : {sample.hex().upper()}")
        print(f"  frame length       : {len(sample)} bytes, checksum verified={parse_ack(sample) is not None}")
        return 0

    devices = [SimulatedDevice(imei, args.host, args.port, args.timeout) for imei in imeis]
    started = time.monotonic()
    print()
    try:
        SCENARIOS[args.scenario](devices, args.steps, lambda line: print(line))
    except (OSError, RuntimeError) as exc:
        print(f"\nscenario aborted: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 1
    finally:
        for dev in devices:
            dev.close()

    elapsed = time.monotonic() - started
    sent = sum(d.result.frames_sent for d in devices)
    acked = sum(d.result.acks_received for d in devices)
    bad = sum(d.result.acks_bad_crc for d in devices)
    errors = [f"{d.imei}: {e}" for d in devices for e in d.result.errors]

    print(f"\nframes sent            : {sent}")
    print(f"acknowledgements read  : {acked}")
    print(f"acks failing their CRC : {bad}")
    print(f"elapsed                : {elapsed:.2f}s")
    if errors:
        print("issues:")
        for line in errors[:20]:
            print(f"  {line}")
    return 1 if bad or errors else 0


def self_test() -> int:
    """Offline checks. No socket is opened."""
    failures: list[str] = []

    def check(name: str, ok: bool, detail: str = "") -> None:
        print(f"  [{'ok' if ok else 'FAIL'}] {name}{'  ' + detail if detail else ''}")
        if not ok:
            failures.append(name)

    print("GT06 device simulator self-test")
    check("CRC-16/X.25 canonical check value", crc16_itu(b"123456789") == 0x906E,
          f"0x{crc16_itu(b'123456789'):04X}")

    imei = "868120303337976"
    packed = pack_imei(imei)
    check("IMEI packs to 8 BCD bytes", packed.hex().upper() == "0868120303337976", packed.hex().upper())
    check("leading-zero IMEI packs losslessly",
          pack_imei("012345678901234").hex().upper() == "0012345678901234")

    frame = login_frame(imei)
    parsed = parse_ack(frame)
    check("built login frame verifies its own checksum", parsed is not None and parsed["crc_ok"])
    check("login frame carries protocol 0x01", parsed is not None and parsed["protocol"] == PROTO_LOGIN)
    check("corrupting the checksum is detected",
          (bad := parse_ack(corrupt_checksum(frame))) is not None and not bad["crc_ok"])

    now = _dt.datetime(2024, 1, 15, 10, 20, 30)
    for name, lat, lng, north, west in [
        ("north/east", 35.6762, 139.6503, True, False),
        ("north/west", 40.7128, -74.0060, True, True),
        ("south/east", -33.8688, 151.2093, False, False),
        ("south/west", -34.6037, -58.3816, False, True),
    ]:
        f = location_frame(1, now, lat, lng)
        status = int.from_bytes(f[4 + 16:4 + 18], "big")
        ok = bool(status & BIT_NORTH) == north and bool(status & BIT_WEST) == west
        check(f"hemisphere bits for {name}", ok, f"word=0x{status:04X}")

    hb_connected = heartbeat_frame(1, oil_connected=True)
    hb_cut = heartbeat_frame(1, oil_connected=False)
    check("terminal-info bit7 set only when oil/electricity is CUT",
          not (hb_connected[4] & 0x80) and bool(hb_cut[4] & 0x80))

    check("frame refuses an out-of-range serial",
          _raises(lambda: build_frame(PROTO_LOGIN, b"", 0x10000)))
    check("simulator refuses production", _raises(
        lambda: validate_target("x.example", 5023, "production", set(), None, True)))
    check("simulator refuses an unlisted non-loopback target", _raises(
        lambda: validate_target("gateway.example", 5023, "staging", set(), None, True)))
    check("simulator refuses a known production host", _raises(
        lambda: validate_target("opstrax.vercel.app", 5023, "staging", {"opstrax.vercel.app"}, SEND_ACK, True)))
    check("loopback needs no acknowledgement",
          not _raises(lambda: validate_target("127.0.0.1", 5023, "local", set(), None, True)))

    ids = synthetic_imeis("868120300000000", 3)
    check("synthetic IMEIs are distinct and 15 digits",
          len(set(ids)) == 3 and all(len(i) == 15 and i.isdigit() for i in ids), ", ".join(ids))

    print(f"\n{'all checks passed' if not failures else str(len(failures)) + ' FAILED: ' + ', '.join(failures)}")
    return 0 if not failures else 1


def _raises(fn) -> bool:
    try:
        fn()
    except Exception:
        return True
    return False


if __name__ == "__main__":
    raise SystemExit(main())
