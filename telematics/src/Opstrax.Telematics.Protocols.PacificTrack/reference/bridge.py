#!/usr/bin/env python3
"""Reference stdio bridge between the OpsTrax telematics edge and Pacific Track's parser.

This file implements the OpsTrax side of the protocol completely and correctly: framing,
hex handling, error reporting, and the exact response shapes the gateway expects. What it
deliberately does NOT contain is a Pacific Track decoder.

    >>> Fill in identify() and decode() with calls to the vendor parser. <<<

Until you do, the bridge answers every request with a clear error, the adapter above it
refuses PT streams, and PT devices are counted as refused rather than mis-decoded. That is
the intended state -- see ../README.md for why OpsTrax does not guess at a licensed wire
format.

Run it manually to check your wiring:

    echo '{"op":"identify","hex":"242401"}' | python3 bridge.py

Protocol: one JSON object per line in, exactly one JSON object per line out, UTF-8.
"""

from __future__ import annotations

import json
import sys
import traceback
from typing import Any

# The vendor SDK. Import it here once it is installed, e.g.:
#   from pacifictrack import Parser
#   _VENDOR = Parser()
_VENDOR = None

PARSER_NOT_WIRED = (
    "Pacific Track parser is not wired into bridge.py. Implement identify() and decode() "
    "against the vendor SDK; see ../README.md."
)


# ── Fill these in ─────────────────────────────────────────────────────────────

def identify(buffer: bytes) -> dict[str, Any]:
    """Decide whether `buffer` is the start of a Pacific Track stream.

    Return one of:
        {"match": True, "confidence": 0.98}   # recognised
        {"match": False}                      # definitively not PT
        {"needMoreData": True}                # too few bytes to tell yet

    Confidence arbitrates when several adapters claim the same opening bytes. Report an
    honest number: a tie between two adapters is refused rather than guessed, which is the
    behaviour you want when a fingerprint really is ambiguous.
    """
    raise NotImplementedError(PARSER_NOT_WIRED)


def decode(buffer: bytes) -> dict[str, Any]:
    """Decode every COMPLETE frame in `buffer`.

    Return:
        {
            "consumed": <leading bytes fully consumed; 0 if no frame completed>,
            "frames": [
                {
                    "type": "Location",          # Login|Heartbeat|Location|Alarm|Status|Ack|Unknown
                    "hex": "7878...",            # EXACT bytes of this one frame (replay hashing)
                    "messageId": 7,              # protocol serial, when the frame carries one
                    "requiresAck": True,
                    "imei": "862464068456321",   # when the frame carries an identity claim
                    "fields": {
                        "latitude": 34.05,
                        "longitude": -118.24,
                        "speedKph": 52,
                        "courseDeg": 91,
                        "fixTimeUtc": "2026-08-21T12:00:00Z",   # MANDATORY on location frames
                        "ignitionOn": True,
                        "alarmName": "sos",
                    },
                }
            ],
        }

    Raise ValueError for input that is malformed beyond recovery (bad checksum, impossible
    framing). The gateway turns that into a ProtocolException, drops that one connection, and
    keeps serving every other tracker -- it never fabricates a fix from corrupt bytes.

    Never invent fixTimeUtc from the current clock. A fix with no device clock must be reported
    without one; the gateway rejects it deliberately, because passing arrival time off as a
    device fix time relabels an offline-buffered frame as live.
    """
    raise NotImplementedError(PARSER_NOT_WIRED)


def encode_ack(frame: bytes, message_id: int | None) -> bytes:
    """Build the acknowledgement the protocol expects for `frame`.

    Return b"" when the protocol requires no acknowledgement for that frame type.
    """
    raise NotImplementedError(PARSER_NOT_WIRED)


# ── Protocol plumbing (complete; you should not need to change this) ──────────

def _handle(request: dict[str, Any]) -> dict[str, Any]:
    op = request.get("op")
    buffer = bytes.fromhex(request.get("hex", ""))

    if op == "identify":
        result = identify(buffer)
        return {
            "ok": True,
            "match": bool(result.get("match", False)),
            "confidence": float(result.get("confidence", 1.0)),
            "needMoreData": bool(result.get("needMoreData", False)),
        }

    if op == "decode":
        result = decode(buffer)
        frames = result.get("frames", []) or []
        # Never claim more than was supplied: the gateway clamps this too, but reporting it
        # honestly keeps the two sides' views of the buffer identical.
        consumed = max(0, min(int(result.get("consumed", 0)), len(buffer)))
        return {"ok": True, "consumed": consumed, "frames": frames}

    if op == "ack":
        ack = encode_ack(buffer, request.get("messageId"))
        return {"ok": True, "hex": ack.hex().upper()}

    return {"ok": False, "error": f"unknown op {op!r}"}


def main() -> int:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        try:
            response = _handle(json.loads(line))
        except ValueError as bad_bytes:
            # The BYTES are bad -- a normal, per-connection outcome.
            response = {"ok": False, "error": str(bad_bytes)}
        except NotImplementedError as not_wired:
            response = {"ok": False, "error": str(not_wired)}
        except Exception:  # noqa: BLE001 - a crash here would wedge the whole edge
            # stderr is drained by the gateway and forwarded to its log, so this is visible.
            traceback.print_exc(file=sys.stderr)
            response = {"ok": False, "error": "parser raised an unexpected exception"}

        # Exactly one line out per line in, flushed: the gateway pairs them positionally, and
        # falling behind by one response desynchronizes every exchange after it.
        sys.stdout.write(json.dumps(response, separators=(",", ":")) + "\n")
        sys.stdout.flush()

    return 0


if __name__ == "__main__":
    sys.exit(main())
