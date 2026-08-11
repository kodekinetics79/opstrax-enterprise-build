#!/usr/bin/env python3
"""
PT40 / generic GPS-tracker raw TCP capture listener.

PURPOSE
    Stand up a throwaway TCP endpoint that a GPS tracker can be pointed at, and
    record EXACTLY what bytes it sends -- nothing more. The captured hex is what
    lets us fingerprint the real protocol deterministically instead of guessing.

    This is a CAPTURE tool, not the gateway. It does not decode, does not touch
    the database, and does not know what a tenant is. Its whole job is: accept a
    socket, write down the bytes, never crash.

USAGE
    # 1) Passive capture (default, recommended for the FIRST contact).
    python3 capture_listener.py --port 5023 --out capture.hex

    # 2) After fingerprinting confirms GT06/Concox, you may need to ACK the
    #    login packet before the device will send LOCATION packets:
    python3 capture_listener.py --port 5023 --out capture.hex --gt06-ack

    Then feed the capture to the fingerprinter:
    python3 fingerprint.py capture.hex

WHY PASSIVE FIRST
    Most binary trackers (GT06/Concox family included) open the connection with a
    LOGIN packet and then wait for a server acknowledgement. The login packet
    alone is enough to (a) identify the protocol family and (b) confirm the IMEI
    is our device -- which is all we need to fingerprint. Only once we KNOW the
    protocol should we start replying to it. Replying with a guessed ACK format
    is how you get a device that silently disconnects and retries forever.

SAFETY
    - Read-only with respect to the device: passive mode never writes to the socket.
    - No external dependencies (standard library only).
    - A malformed / hostile / oversized payload cannot crash the listener; each
      connection is isolated.
    - Peer IPs are written to the local capture log for debugging. Redact them
      before sharing the log outside your team.

EXIT
    Ctrl-C. Captures are flushed to --out as they arrive, so an interrupted run
    still leaves you a usable file.
"""

from __future__ import annotations

import argparse
import binascii
import datetime as _dt
import os
from pathlib import Path
import socket
import socketserver
import sys
import threading

MAX_READ = 4096          # per-recv cap
MAX_CONN_BYTES = 1 << 20 # 1 MiB per connection, then we stop recording (flood guard)
CAPTURE_ROOT = (Path(__file__).resolve().parent / "captures").resolve()
PUBLIC_BIND_ACK = "I_UNDERSTAND_THIS_EXPOSES_A_SYNTHETIC_CAPTURE_PORT"
ACTIVE_ACK = "I_CONFIRMED_GT06_AND_ACCEPT_DEVICE_REPLIES"

_write_lock = threading.Lock()


def _utc_now() -> str:
    return _dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")


def crc_itu(data: bytes) -> int:
    """CRC-16/X.25 (a.k.a. CRC-ITU) as used by the GT06/Concox frame format.

    Reflected poly 0x8408, init 0xFFFF, final XOR 0xFFFF.
    Only used when --gt06-ack is explicitly enabled.
    """
    crc = 0xFFFF
    for b in data:
        crc ^= b
        for _ in range(8):
            if crc & 1:
                crc = (crc >> 1) ^ 0x8408
            else:
                crc >>= 1
    return (~crc) & 0xFFFF


def build_gt06_ack(protocol: int, serial: bytes) -> bytes:
    """Server ACK for a GT06 login/heartbeat frame.

    Frame: 78 78 | len=05 | protocol | serial(2) | crc(2) | 0D 0A
    CRC is computed over [len, protocol, serial].
    """
    body = bytes([0x05, protocol]) + serial
    crc = crc_itu(body)
    return b"\x78\x78" + body + crc.to_bytes(2, "big") + b"\x0d\x0a"


def try_gt06_ack(chunk: bytes) -> bytes | None:
    """If `chunk` looks like a GT06 0x7878 frame, build the matching ACK.

    Deliberately conservative: only handles the 0x7878 (1-byte length) form and
    only ACKs login (0x01) and heartbeat/status (0x13/0x23). Anything else -> None.
    """
    if len(chunk) < 10 or chunk[0:2] != b"\x78\x78":
        return None
    length = chunk[2]
    frame_end = 3 + length
    total = frame_end + 2
    if length < 5 or len(chunk) < total:
        return None
    if chunk[frame_end:total] != b"\x0d\x0a":
        return None
    protocol = chunk[3]
    if protocol not in (0x01, 0x13, 0x23):
        return None
    crc_pos = frame_end - 2
    found_crc = int.from_bytes(chunk[crc_pos:frame_end], "big")
    if crc_itu(chunk[2:crc_pos]) != found_crc:
        return None
    # serial sits immediately before the 2-byte CRC.
    serial = chunk[crc_pos - 2 : crc_pos]
    if len(serial) != 2:
        return None
    return build_gt06_ack(protocol, serial)


def resolve_capture_path(value: str) -> Path:
    """Confine captures to the ignored tool capture directory."""
    candidate = Path(value)
    if candidate.is_absolute():
        resolved = candidate.resolve()
    else:
        resolved = (CAPTURE_ROOT / candidate).resolve()
    if resolved != CAPTURE_ROOT and CAPTURE_ROOT not in resolved.parents:
        raise ValueError(f"capture output must stay under {CAPTURE_ROOT}")
    return resolved


def prepare_capture_file(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    os.chmod(path.parent, 0o700)
    if path.exists() and (path.stat().st_mode & 0o077):
        raise ValueError(f"existing capture must be mode 0600 (or stricter): {path}")


def append_capture(path: Path, line: str) -> None:
    fd = os.open(path, os.O_APPEND | os.O_CREAT | os.O_WRONLY, 0o600)
    try:
        os.write(fd, (line + "\n").encode("utf-8"))
    finally:
        os.close(fd)


def bounded_capture_chunk(chunk: bytes, already_recorded: int) -> tuple[bytes, int, int]:
    """Return the recordable prefix, new total, and dropped-byte count at the exact cap."""
    remaining = max(0, MAX_CONN_BYTES - already_recorded)
    recorded = chunk[:remaining]
    return recorded, already_recorded + len(recorded), len(chunk) - len(recorded)


def validate_config(cfg: argparse.Namespace) -> None:
    if cfg.environment == "production":
        raise ValueError("capture listener refuses production")
    if not 1 <= cfg.port <= 65535:
        raise ValueError("port must be between 1 and 65535")
    if not 1 <= cfg.idle_timeout <= 600:
        raise ValueError("idle timeout must be between 1 and 600 seconds")
    loopback = cfg.host.lower() in {"127.0.0.1", "localhost", "::1"}
    if not loopback:
        if cfg.environment != "staging" or not cfg.public_staging or cfg.public_bind_ack != PUBLIC_BIND_ACK:
            raise ValueError("non-loopback capture requires staging, --public-staging, and the exact public-bind acknowledgement")
    if cfg.gt06_ack and (cfg.confirmed_protocol != "GT06" or cfg.active_ack != ACTIVE_ACK):
        raise ValueError("GT06 replies require confirmed protocol and the exact active-ack acknowledgement")
    cfg.out = resolve_capture_path(cfg.out)
    prepare_capture_file(cfg.out)


class CaptureHandler(socketserver.BaseRequestHandler):
    def handle(self) -> None:  # noqa: C901 - readability over cleverness here
        peer = f"{self.client_address[0]}:{self.client_address[1]}"
        cfg = self.server.cfg  # type: ignore[attr-defined]
        total = 0
        frame_no = 0

        self._emit(f"# === CONNECTION OPEN {_utc_now()} peer={peer} ===")
        print(f"[{_utc_now()}] CONNECT  {peer}", flush=True)

        try:
            self.request.settimeout(cfg.idle_timeout)
            while True:
                try:
                    chunk = self.request.recv(MAX_READ)
                except socket.timeout:
                    self._emit(f"# idle timeout after {cfg.idle_timeout}s peer={peer}")
                    break
                except OSError as exc:
                    self._emit(f"# socket error peer={peer}: {exc!r}")
                    break

                if not chunk:
                    break  # clean close

                recorded, total, dropped = bounded_capture_chunk(chunk, total)
                if not recorded:
                    self._emit(f"# flood guard: {total} bytes from {peer}, dropped={dropped}, closing")
                    break

                frame_no += 1
                hexs = binascii.hexlify(recorded).decode()
                ascii_preview = "".join(
                    (chr(b) if 32 <= b < 127 else ".") for b in recorded[:64]
                )

                # The capture file is what you send me: one hex payload per line.
                self._emit(hexs)
                self._emit(
                    f"#   ^ frame={frame_no} bytes={len(recorded)} peer={peer} "
                    f"at={_utc_now()} ascii={ascii_preview!r}"
                )
                print(
                    f"[{_utc_now()}] RX {len(recorded):4d}B from {peer}\n"
                    f"    HEX   {hexs}\n"
                    f"    ASCII {ascii_preview}",
                    flush=True,
                )

                if cfg.gt06_ack and dropped == 0:
                    ack = try_gt06_ack(recorded)
                    if ack:
                        try:
                            self.request.sendall(ack)
                            self._emit(f"#   -> sent GT06 ACK {binascii.hexlify(ack).decode()}")
                            print(
                                f"    ACK-> {binascii.hexlify(ack).decode()}", flush=True
                            )
                        except OSError as exc:
                            self._emit(f"# ack send failed peer={peer}: {exc!r}")
                            break

                if dropped or total >= MAX_CONN_BYTES:
                    self._emit(f"# flood guard: {total} bytes from {peer}, dropped={dropped}, closing")
                    break
        except Exception as exc:  # never let one connection kill the listener
            self._emit(f"# handler exception peer={peer}: {exc!r}")
        finally:
            self._emit(f"# === CONNECTION CLOSE {_utc_now()} peer={peer} bytes={total} ===")
            print(f"[{_utc_now()}] CLOSE    {peer} ({total} bytes)", flush=True)

    def _emit(self, line: str) -> None:
        cfg = self.server.cfg  # type: ignore[attr-defined]
        if not cfg.out:
            return
        with _write_lock:
            append_capture(cfg.out, line)


class Listener(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Raw TCP capture listener for GPS trackers (PT40 fingerprinting)."
    )
    ap.add_argument("--host", default="127.0.0.1", help="bind address (default: loopback only)")
    ap.add_argument("--port", type=int, default=5023, help="bind port (default: 5023)")
    ap.add_argument("--out", default="capture.hex", help="capture filename under tools/telematics/captures/")
    ap.add_argument("--environment", choices=("local", "staging", "production"), default="local")
    ap.add_argument("--public-staging", action="store_true", help="allow a non-loopback staging bind")
    ap.add_argument("--public-bind-ack", default="", help=argparse.SUPPRESS)
    ap.add_argument(
        "--idle-timeout",
        type=float,
        default=300.0,
        help="seconds of silence before closing a connection (default: 300)",
    )
    ap.add_argument(
        "--gt06-ack",
        action="store_true",
        help="reply to GT06 login/heartbeat frames (ONLY after fingerprint confirms GT06)",
    )
    ap.add_argument("--confirmed-protocol", choices=("GT06",), help="required with --gt06-ack")
    ap.add_argument("--active-ack", default="", help=argparse.SUPPRESS)
    cfg = ap.parse_args()

    try:
        validate_config(cfg)
    except ValueError as exc:
        ap.error(str(exc))

    srv = Listener((cfg.host, cfg.port), CaptureHandler)
    srv.cfg = cfg  # type: ignore[attr-defined]

    mode = "GT06-ACK" if cfg.gt06_ack else "PASSIVE (no bytes sent to device)"
    print(
        f"PT40 capture listener\n"
        f"  listening : {cfg.host}:{cfg.port}\n"
        f"  mode      : {mode}\n"
        f"  capture   : {cfg.out}\n"
        f"  waiting for the tracker to connect... (Ctrl-C to stop)\n",
        flush=True,
    )

    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        print("\nstopping...", flush=True)
    finally:
        srv.shutdown()
        srv.server_close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
