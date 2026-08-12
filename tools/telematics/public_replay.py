#!/usr/bin/env python3
"""Replay one committed synthetic telematics fixture to an explicitly allowed staging TCP host.

The default is a zero-network dry-run. The tool refuses production, real capture paths,
multiple sends, and payloads larger than the fingerprinter's 1 MiB cap.
"""

from __future__ import annotations

import argparse
from contextlib import closing
import hashlib
import json
from pathlib import Path
import socket
import ssl
import subprocess
import sys
from typing import Callable, Optional

from fingerprint import CONFIRMED, MAX_CAPTURE_BYTES, fingerprint, read_capture


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
FIXTURE_ROOT = (REPOSITORY_ROOT / "telematics" / "fixtures" / "gt06").resolve()
KNOWN_PRODUCTION_HOSTS = frozenset({
    "opstrax.vercel.app",
    "osptrax-fleet-management.onrender.com",
})
SEND_ACK = "I_UNDERSTAND_THIS_SENDS_ONE_SYNTHETIC_FRAME"
NEGATIVE_ACK = "I_UNDERSTAND_THIS_FIXTURE_IS_INTENTIONALLY_INVALID"


def resolve_fixture(value: str) -> Path:
    candidate = Path(value)
    resolved = candidate.resolve() if candidate.is_absolute() else (FIXTURE_ROOT / candidate).resolve()
    if resolved == FIXTURE_ROOT or FIXTURE_ROOT not in resolved.parents:
        raise ValueError(f"fixture must stay under {FIXTURE_ROOT}")
    if resolved.suffix != ".hex" or not resolved.is_file():
        raise ValueError("fixture must be an existing committed .hex file")
    if resolved.stat().st_size > MAX_CAPTURE_BYTES:
        raise ValueError("fixture exceeds the 1 MiB safety limit")
    relative = resolved.relative_to(REPOSITORY_ROOT).as_posix()
    try:
        tracked = subprocess.run(
            ["git", "ls-files", "--error-unmatch", "--", relative],
            cwd=REPOSITORY_ROOT,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
            timeout=5,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise ValueError("unable to verify committed fixture membership") from exc
    if tracked.returncode != 0:
        raise ValueError("fixture is not tracked by git; public replay accepts committed fixtures only")
    return resolved


def validate_target(
    host: str,
    port: int,
    environment: str,
    allowed_hosts: set[str],
    production_hosts: set[str],
) -> None:
    normalized = host.strip().lower().rstrip(".")
    if environment != "staging":
        raise ValueError("public replay requires environment=staging and refuses production")
    if not normalized or any(character in normalized for character in "/:@"):
        raise ValueError("host must be a bare hostname or IP address")
    if not 1 <= port <= 65535:
        raise ValueError("port must be between 1 and 65535")
    if normalized in production_hosts:
        raise ValueError("public replay refuses a known production host")
    if normalized not in allowed_hosts:
        raise ValueError("target host must be explicitly listed by --allow-host")


def replay_once(
    host: str,
    port: int,
    payload: bytes,
    *,
    timeout: float = 5.0,
    use_tls: bool = False,
    receive_bytes: int = 0,
    connector: Callable[..., socket.socket] = socket.create_connection,
    tls_context_factory: Callable[[], ssl.SSLContext] = ssl.create_default_context,
) -> bytes:
    if not payload or len(payload) > MAX_CAPTURE_BYTES:
        raise ValueError("payload must contain between 1 byte and 1 MiB")
    with closing(connector((host, port), timeout=timeout)) as raw_socket:
        transport = raw_socket
        if use_tls:
            transport = tls_context_factory().wrap_socket(raw_socket, server_hostname=host)
        try:
            transport.settimeout(timeout)
            transport.sendall(payload)
            return transport.recv(receive_bytes) if receive_bytes else b""
        finally:
            if transport is not raw_socket:
                transport.close()


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="One-shot synthetic telematics fixture replay (staging only).")
    parser.add_argument("--fixture", required=True, help="filename under telematics/fixtures/gt06/")
    parser.add_argument("--host", required=True, help="bare staging hostname or IP")
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--environment", choices=("staging", "production", "local"), required=True)
    parser.add_argument("--allow-host", action="append", default=[], help="exact allowed staging host; repeatable")
    parser.add_argument("--production-host", action="append", default=[], help="additional production hostname to refuse")
    parser.add_argument("--tls", action="store_true", help="use certificate-validated TLS")
    parser.add_argument("--timeout", type=float, default=5.0)
    parser.add_argument("--receive-bytes", type=int, default=0, help="read at most this many ACK bytes (0-4096)")
    parser.add_argument("--allow-negative-fixture", action="store_true")
    parser.add_argument("--negative-ack", default="", help=argparse.SUPPRESS)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--dry-run", action="store_true", help="validate and fingerprint without DNS or socket calls")
    mode.add_argument("--send", action="store_true", help="send exactly one fixture once")
    parser.add_argument("--send-ack", default="", help=argparse.SUPPRESS)
    return parser


def main(argv: Optional[list[str]] = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    try:
        if not 0.1 <= args.timeout <= 30:
            raise ValueError("timeout must be between 0.1 and 30 seconds")
        if not 0 <= args.receive_bytes <= 4096:
            raise ValueError("receive-bytes must be between 0 and 4096")
        allowed = {item.strip().lower().rstrip(".") for item in args.allow_host if item.strip()}
        production = set(KNOWN_PRODUCTION_HOSTS)
        production.update(item.strip().lower().rstrip(".") for item in args.production_host if item.strip())
        validate_target(args.host, args.port, args.environment, allowed, production)
        fixture_path = resolve_fixture(args.fixture)
        payload = read_capture(str(fixture_path))
        verdict = fingerprint(payload)
        if verdict.status != CONFIRMED and not (
            args.allow_negative_fixture and args.negative_ack == NEGATIVE_ACK
        ):
            raise ValueError("fixture is not protocol-confirmed; negative replay requires both explicit negative-fixture controls")
        if args.send and args.send_ack != SEND_ACK:
            raise ValueError("sending requires the exact one-frame acknowledgement")

        response = b""
        network_calls = 0
        if args.send:
            response = replay_once(
                args.host,
                args.port,
                payload,
                timeout=args.timeout,
                use_tls=args.tls,
                receive_bytes=args.receive_bytes,
            )
            network_calls = 1

        result = {
            "mode": "sent" if args.send else "dry-run",
            "environment": "staging",
            "target": {"host": args.host, "port": args.port, "tls": args.tls},
            "fixture": fixture_path.name,
            "protocol": verdict.protocol,
            "fingerprintStatus": verdict.status,
            "payloadBytes": len(payload),
            "payloadSha256": hashlib.sha256(payload).hexdigest(),
            "responseBytes": len(response),
            "responseSha256": hashlib.sha256(response).hexdigest() if response else None,
            "networkCalls": network_calls,
        }
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0
    except (OSError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
