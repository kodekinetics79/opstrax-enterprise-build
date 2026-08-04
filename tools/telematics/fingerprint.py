#!/usr/bin/env python3
"""
fingerprint.py — identify a telematics wire protocol from its first captured bytes.

Reference implementation of the deterministic decision tree in
docs/telematics/pt40/pt40-fingerprint.md. Given a hex string or a capture file, it
walks the branches in order, returns the first branch whose SIGNATURE matches, then
runs that branch's CONFIRM step and prints the rationale.

READ-ONLY: this script opens nothing but the capture you name, writes no files, makes
no network calls, and mutates no state. Standard library only.

Usage:
  ./fingerprint.py 78780d01086286...0d0a          # hex string (positional)
  ./fingerprint.py --hex "78 78 0d 01 ..."        # hex, any spacing/0x/: separators
  ./fingerprint.py --file capture.bin             # raw binary capture
  ./fingerprint.py --file capture.hex             # text file of hex (auto-detected)
  cat capture.bin | ./fingerprint.py --file -     # stdin
  ./fingerprint.py --json 7878...                 # machine-readable output
  ./fingerprint.py --self-test                    # run built-in vectors

Exit codes: 0 = confirmed, 2 = signature matched but CONFIRM failed, 3 = unknown,
            1 = bad input/usage.
"""

from __future__ import annotations

import argparse
import json
import string
import sys
from typing import Callable, List, NamedTuple, Optional, Tuple

# --------------------------------------------------------------------------------------
# Result model
# --------------------------------------------------------------------------------------

CONFIRMED = "confirmed"
UNCONFIRMED = "signature-only"   # signature matched, confirm step failed
UNKNOWN = "unknown"


class Verdict(NamedTuple):
    protocol: str          # e.g. "GT06/Concox"
    status: str            # CONFIRMED | UNCONFIRMED | UNKNOWN
    confidence: float      # 0.0 - 1.0
    adapter: str           # which adapter handles it
    signature: str         # the signature bytes that matched
    rationale: List[str]   # ordered human-readable reasoning
    branch: Optional[int]  # branch number in the decision tree


# --------------------------------------------------------------------------------------
# Byte helpers
# --------------------------------------------------------------------------------------

_HEX_CHARS = set("0123456789abcdefABCDEF")
_SEPARATORS = " \t\r\n:,-_"
_PRINTABLE = set(bytes(string.printable, "ascii"))


def parse_hex(text: str) -> bytes:
    """Parse a hex string tolerating whitespace, 0x prefixes, colons, commas, dashes."""
    cleaned = text.strip()
    if cleaned.lower().startswith("0x"):
        cleaned = cleaned[2:]
    out = []
    for chunk in cleaned.replace("0x", " ").replace("0X", " ").split():
        for ch in chunk:
            if ch in _SEPARATORS:
                continue
            if ch not in _HEX_CHARS:
                raise ValueError("not a hex string (bad char %r)" % ch)
            out.append(ch)
    digits = "".join(out)
    if not digits:
        raise ValueError("empty input")
    if len(digits) % 2:
        raise ValueError("odd number of hex digits (%d)" % len(digits))
    return bytes.fromhex(digits)


def looks_like_hex_text(raw: bytes) -> bool:
    """True when a file's bytes look like a TEXT capture of hex rather than raw bytes."""
    try:
        text = raw.decode("ascii")
    except UnicodeDecodeError:
        return False
    stripped = "".join(c for c in text if c not in _SEPARATORS)
    stripped = stripped.replace("0x", "").replace("0X", "")
    if not stripped:
        return False
    return all(c in _HEX_CHARS for c in stripped)


def read_capture(path: str) -> bytes:
    """Read a capture file (or '-' for stdin); auto-detect hex-text vs raw binary."""
    if path == "-":
        raw = sys.stdin.buffer.read()
    else:
        with open(path, "rb") as fh:
            raw = fh.read()
    if not raw:
        raise ValueError("capture is empty")
    if looks_like_hex_text(raw):
        return parse_hex(raw.decode("ascii"))
    return raw


def hexdump(b: bytes, limit: int = 32) -> str:
    shown = b[:limit]
    out = " ".join("%02X" % x for x in shown)
    if len(b) > limit:
        out += " … (+%d bytes)" % (len(b) - limit)
    return out


def ascii_preview(b: bytes, limit: int = 48) -> str:
    return "".join(chr(x) if x in _PRINTABLE and x >= 0x20 else "." for x in b[:limit])


def starts_with(b: bytes, prefix: bytes) -> bool:
    return len(b) >= len(prefix) and b[: len(prefix)] == prefix


# --------------------------------------------------------------------------------------
# Checksums (used by CONFIRM steps)
# --------------------------------------------------------------------------------------

def crc_itu(data: bytes) -> int:
    """CRC-ITU / CRC-16/X-25 — GT06 family trailer."""
    crc = 0xFFFF
    for byte in data:
        crc ^= byte
        for _ in range(8):
            crc = (crc >> 1) ^ 0x8408 if crc & 1 else crc >> 1
    return (~crc) & 0xFFFF


def crc16_ibm(data: bytes) -> int:
    """CRC-16/IBM (ARC) — Teltonika trailer."""
    crc = 0x0000
    for byte in data:
        crc ^= byte
        for _ in range(8):
            crc = (crc >> 1) ^ 0xA001 if crc & 1 else crc >> 1
    return crc & 0xFFFF


def xor_checksum(data: bytes) -> int:
    """1-byte XOR — JT/T808 and NMEA."""
    acc = 0
    for byte in data:
        acc ^= byte
    return acc & 0xFF


def jt808_unescape(body: bytes) -> bytes:
    """Reverse JT808 byte-stuffing: 7D 02 -> 7E, 7D 01 -> 7D."""
    out = bytearray()
    i = 0
    while i < len(body):
        if body[i] == 0x7D and i + 1 < len(body):
            nxt = body[i + 1]
            if nxt == 0x02:
                out.append(0x7E)
                i += 2
                continue
            if nxt == 0x01:
                out.append(0x7D)
                i += 2
                continue
        out.append(body[i])
        i += 1
    return bytes(out)


# --------------------------------------------------------------------------------------
# Branch 1 — GT06 / Concox / Jimi
# --------------------------------------------------------------------------------------

GT06_ADAPTER = "Opstrax.Telematics.Protocols.Gt06.Gt06ProtocolAdapter (\"GT06\") — placeholder, not yet implementing IProtocolAdapter"


def try_gt06(b: bytes) -> Optional[Verdict]:
    if starts_with(b, b"\x78\x78"):
        extended = False
    elif starts_with(b, b"\x79\x79"):
        extended = True
    else:
        return None

    sig = "79 79" if extended else "78 78"
    why = ["b[0..2] == %s -> GT06/Concox/Jimi %s frame (branch 1)."
           % (sig, "extended (2-byte length)" if extended else "standard (1-byte length)")]
    conf = 0.60
    status = UNCONFIRMED

    # CONFIRM: length field locates the 0D 0A trailer; CRC-ITU over len..serial.
    if extended:
        if len(b) < 4:
            why.append("CONFIRM incomplete: need >=4 bytes to read the 2-byte length.")
            return Verdict("GT06/Concox", UNCONFIRMED, conf, GT06_ADAPTER, sig, why, 1)
        length = (b[2] << 8) | b[3]
        len_field_size = 2
    else:
        if len(b) < 3:
            why.append("CONFIRM incomplete: need >=3 bytes to read the length byte.")
            return Verdict("GT06/Concox", UNCONFIRMED, conf, GT06_ADAPTER, sig, why, 1)
        length = b[2]
        len_field_size = 1

    why.append("Length field = %d (0x%02X); it counts protocol-number byte .. CRC." % (length, length))
    # frame = start(2) + len_field + [length bytes: protocol..crc] + stop(2)
    frame_end = 2 + len_field_size + length          # index of first stop byte
    total = frame_end + 2

    if len(b) < total:
        why.append("CONFIRM incomplete: frame claims %d bytes, only %d captured. "
                   "Capture the full frame (through 0D 0A) to confirm." % (total, len(b)))
        return Verdict("GT06/Concox", UNCONFIRMED, conf, GT06_ADAPTER, sig, why, 1)

    if b[frame_end:frame_end + 2] != b"\x0d\x0a":
        why.append("CONFIRM FAILED: expected stop bytes 0D 0A at offset %d, found %s. "
                   "Length field and framing disagree -> do NOT accept as GT06; re-capture."
                   % (frame_end, hexdump(b[frame_end:frame_end + 2])))
        return Verdict("GT06/Concox", UNCONFIRMED, 0.35, GT06_ADAPTER, sig, why, 1)
    why.append("Stop bytes 0D 0A present at offset %d (as the length field predicted)." % frame_end)

    # CRC-ITU covers the length field through the information serial number (i.e. everything
    # between the start bytes and the 2-byte CRC).
    crc_start = 2
    crc_pos = frame_end - 2
    calculated = crc_itu(b[crc_start:crc_pos])
    found = (b[crc_pos] << 8) | b[crc_pos + 1]
    if calculated == found:
        why.append("CONFIRM PASSED: CRC-ITU over b[2:%d] == 0x%04X, matches trailer." % (crc_pos, found))
        status, conf = CONFIRMED, 0.99
    else:
        why.append("CONFIRM FAILED: CRC-ITU mismatch (computed 0x%04X, frame carries 0x%04X)."
                   % (calculated, found))
        conf = 0.40

    if len(b) >= 2 + len_field_size + 1:
        proto = b[2 + len_field_size]
        names = {0x01: "login (carries BCD IMEI)", 0x12: "location", 0x22: "location (GPS)",
                 0x13: "status/heartbeat", 0x16: "alarm", 0x26: "alarm",
                 0x15: "string/command response", 0x8A: "time sync"}
        why.append("Protocol-number byte = 0x%02X%s."
                   % (proto, " -> %s" % names[proto] if proto in names else " (unmapped message type)"))

    return Verdict("GT06/Concox", status, conf, GT06_ADAPTER, sig, why, 1)


# --------------------------------------------------------------------------------------
# Branch 2 — Teltonika Codec8 / 8E / 16
# --------------------------------------------------------------------------------------

TELTONIKA_ADAPTER = "none in-repo yet — target Opstrax.Telematics.Protocols.Teltonika"
CODECS = {0x08: "Codec8", 0x8E: "Codec8 Extended", 0x10: "Codec16",
          0x0C: "Codec12 (GPRS command)", 0x0D: "Codec13", 0x0F: "Codec15"}


def try_teltonika(b: bytes) -> Optional[Verdict]:
    # 2a. IMEI handshake — the actual FIRST packet a Teltonika device sends: 00 0F + 15 digits.
    if starts_with(b, b"\x00\x0f"):
        why = ["b[0..2] == 00 0F -> Teltonika IMEI handshake: 2-byte length 15 (branch 2)."]
        imei = b[2:17]
        if len(imei) == 15 and all(0x30 <= c <= 0x39 for c in imei):
            why.append("CONFIRM PASSED: exactly 15 ASCII digits follow -> IMEI '%s'."
                       % imei.decode("ascii"))
            why.append("This is first-contact only; the server replies 0x01 to accept, then "
                       "AVL data packets arrive with the 00 00 00 00 preamble.")
            return Verdict("Teltonika (IMEI handshake)", CONFIRMED, 0.97,
                           TELTONIKA_ADAPTER, "00 0F", why, 2)
        why.append("CONFIRM FAILED: expected 15 ASCII digits after 00 0F, got %s."
                   % hexdump(b[2:17]))
        return Verdict("Teltonika (IMEI handshake)", UNCONFIRMED, 0.30,
                       TELTONIKA_ADAPTER, "00 0F", why, 2)

    # 2b. AVL data packet — 4-byte zero preamble.
    if not starts_with(b, b"\x00\x00\x00\x00"):
        return None

    why = ["b[0..4] == 00 00 00 00 -> Teltonika AVL data packet zero-preamble (branch 2)."]
    if len(b) < 9:
        why.append("CONFIRM incomplete: need >=9 bytes (preamble + 4-byte length + codec id).")
        return Verdict("Teltonika", UNCONFIRMED, 0.55, TELTONIKA_ADAPTER, "00 00 00 00", why, 2)

    data_len = int.from_bytes(b[4:8], "big")
    codec = b[8]
    why.append("Data Field Length = %d (b[4:8], big-endian)." % data_len)

    if codec not in CODECS:
        why.append("CONFIRM FAILED: codec id b[8] = 0x%02X is not a known Teltonika codec "
                   "(08 / 8E / 10 expected). Do NOT accept; re-capture." % codec)
        return Verdict("Teltonika", UNCONFIRMED, 0.35, TELTONIKA_ADAPTER, "00 00 00 00", why, 2)
    why.append("Codec ID b[8] = 0x%02X -> %s." % (codec, CODECS[codec]))

    status, conf = UNCONFIRMED, 0.75
    total = 8 + data_len + 4  # preamble+len, data field, 4-byte CRC
    if len(b) < total:
        why.append("CONFIRM incomplete: packet claims %d bytes total, only %d captured "
                   "(need the trailing CRC-16/IBM to fully confirm)." % (total, len(b)))
    else:
        records_1 = b[9]
        records_2 = b[8 + data_len - 1]
        found = int.from_bytes(b[8 + data_len:8 + data_len + 4], "big")
        calculated = crc16_ibm(b[8:8 + data_len])
        if records_1 == records_2:
            why.append("Record count matches at both ends (data 1 = data 2 = %d)." % records_1)
        else:
            why.append("WARNING: record count mismatch (data 1 = %d, data 2 = %d)."
                       % (records_1, records_2))
        if calculated == found and records_1 == records_2:
            why.append("CONFIRM PASSED: CRC-16/IBM over the data field == 0x%08X, matches trailer."
                       % found)
            status, conf = CONFIRMED, 0.99
        else:
            why.append("CONFIRM FAILED: CRC-16/IBM mismatch (computed 0x%04X, frame carries 0x%08X)."
                       % (calculated, found))
            conf = 0.40

    return Verdict("Teltonika %s" % CODECS[codec], status, conf,
                   TELTONIKA_ADAPTER, "00 00 00 00", why, 2)


# --------------------------------------------------------------------------------------
# Branch 3 — JT/T808
# --------------------------------------------------------------------------------------

JT808_ADAPTER = "none in-repo yet — target Opstrax.Telematics.Protocols.Jt808"


def try_jt808(b: bytes) -> Optional[Verdict]:
    if not starts_with(b, b"\x7e"):
        return None

    why = ["b[0] == 7E -> JT/T808 start flag (branch 3)."]
    end = b.find(b"\x7e", 1)
    if end == -1:
        why.append("CONFIRM incomplete: no closing 7E flag found. JT808 frames are "
                   "7E-delimited at BOTH ends — capture the full frame.")
        return Verdict("JT/T808", UNCONFIRMED, 0.50, JT808_ADAPTER, "7E", why, 3)
    why.append("Closing 7E flag found at offset %d -> framed body is b[1:%d]." % (end, end))

    body = jt808_unescape(b[1:end])
    if len(body) != len(b[1:end]):
        why.append("Body was byte-stuffed (7D 02 -> 7E, 7D 01 -> 7D); un-escaped to %d bytes."
                   % len(body))
    if len(body) < 2:
        why.append("CONFIRM FAILED: un-escaped body too short to carry header + checksum.")
        return Verdict("JT/T808", UNCONFIRMED, 0.35, JT808_ADAPTER, "7E", why, 3)

    payload, found = body[:-1], body[-1]
    calculated = xor_checksum(payload)
    if calculated == found:
        why.append("CONFIRM PASSED: 1-byte XOR checksum over the un-escaped body == 0x%02X, "
                   "matches the byte before the closing flag." % found)
        msg_id = int.from_bytes(payload[0:2], "big") if len(payload) >= 2 else 0
        names = {0x0100: "terminal registration", 0x0102: "terminal auth",
                 0x0002: "terminal heartbeat", 0x0200: "location report",
                 0x8001: "platform general response"}
        why.append("Message ID = 0x%04X%s." % (msg_id, " -> %s" % names[msg_id] if msg_id in names else ""))
        return Verdict("JT/T808", CONFIRMED, 0.97, JT808_ADAPTER, "7E", why, 3)

    why.append("CONFIRM FAILED: XOR checksum mismatch (computed 0x%02X, frame carries 0x%02X). "
               "A bare 7E is not enough — do NOT accept." % (calculated, found))
    return Verdict("JT/T808", UNCONFIRMED, 0.40, JT808_ADAPTER, "7E", why, 3)


# --------------------------------------------------------------------------------------
# Branch 4 — Meitrack
# --------------------------------------------------------------------------------------

MEITRACK_ADAPTER = "none in-repo yet — target Opstrax.Telematics.Protocols.Meitrack"


def try_meitrack(b: bytes) -> Optional[Verdict]:
    if not starts_with(b, b"$$"):
        return None

    why = ["b[0..2] == 24 24 (\"$$\") -> Meitrack (branch 4). Second byte is '$', not a "
           "talker letter, so this is NOT NMEA."]
    star = b.rfind(b"*")
    if star == -1:
        why.append("CONFIRM incomplete: no '*' checksum delimiter found — capture the full frame "
                   "(ends '*hh\\r\\n').")
        return Verdict("Meitrack", UNCONFIRMED, 0.60, MEITRACK_ADAPTER, "$$", why, 4)

    tail = b[star + 1:]
    if len(tail) < 2:
        why.append("CONFIRM FAILED: '*' present but no 2-digit checksum follows.")
        return Verdict("Meitrack", UNCONFIRMED, 0.40, MEITRACK_ADAPTER, "$$", why, 4)

    hex_pair = tail[:2]
    try:
        found = int(hex_pair.decode("ascii"), 16)
    except (UnicodeDecodeError, ValueError):
        why.append("CONFIRM FAILED: bytes after '*' (%s) are not 2 ASCII hex digits."
                   % hexdump(hex_pair))
        return Verdict("Meitrack", UNCONFIRMED, 0.40, MEITRACK_ADAPTER, "$$", why, 4)

    calculated = sum(b[: star + 1]) & 0xFF  # sum of all bytes from first '$' through '*'
    fields = b[:star].split(b",")
    if len(fields) >= 2:
        why.append("Comma fields present; field[1] (IMEI slot) = %r." % fields[1][:20])
    if calculated == found:
        why.append("CONFIRM PASSED: checksum over bytes[0..'*'] == 0x%02X, matches the "
                   "2 ASCII hex digits after '*'." % found)
        return Verdict("Meitrack", CONFIRMED, 0.95, MEITRACK_ADAPTER, "$$", why, 4)

    why.append("CONFIRM WEAK: checksum mismatch (computed 0x%02X, frame carries 0x%02X). "
               "Meitrack firmwares vary between sum and XOR here — verify against the model's "
               "doc before accepting." % (calculated, found))
    if calculated != found and xor_checksum(b[: star + 1]) == found:
        why.append("NOTE: an XOR (rather than sum) over the same range DOES match 0x%02X." % found)
        return Verdict("Meitrack", CONFIRMED, 0.90, MEITRACK_ADAPTER, "$$", why, 4)
    return Verdict("Meitrack", UNCONFIRMED, 0.55, MEITRACK_ADAPTER, "$$", why, 4)


# --------------------------------------------------------------------------------------
# Branch 5 — Queclink
# --------------------------------------------------------------------------------------

QUECLINK_ADAPTER = "none in-repo yet — target Opstrax.Telematics.Protocols.Queclink"
QUECLINK_HEADERS = (b"+RESP:", b"+ACK:", b"+BUFF:")


def try_queclink(b: bytes) -> Optional[Verdict]:
    header = None
    for h in QUECLINK_HEADERS:
        if starts_with(b, h):
            header = h
            break
    if header is None:
        return None

    label = header.decode("ascii")
    why = ["b[0] == 2B ('+') and the frame opens with %r -> Queclink (branch 5)." % label]
    kind = {"+RESP:": "spontaneous report", "+ACK:": "acknowledgement",
            "+BUFF:": "buffered/replayed report (device was offline when it occurred)"}[label]
    why.append("Header type: %s." % kind)

    body = b[len(header):]
    token = body.split(b",")[0]
    if token:
        why.append("Report token = %r (e.g. GTFRI fixed-report, GTGEO geofence, GTHBD heartbeat)."
                   % token.decode("ascii", "replace"))

    stripped = b.rstrip(b"\r\n")
    if stripped.endswith(b"$"):
        why.append("CONFIRM PASSED: record is comma-delimited ASCII terminated by '$' (0x24).")
        fields = stripped[:-1].split(b",")
        why.append("Field count = %d; the last field before '$' is the message count/serial "
                   "used to build the +SACK acknowledgement." % len(fields))
        return Verdict("Queclink", CONFIRMED, 0.95, QUECLINK_ADAPTER, label, why, 5)

    why.append("CONFIRM incomplete: no '$' terminator seen. Capture the full record — a "
               "Queclink frame ends with '$' (optionally + CRLF).")
    return Verdict("Queclink", UNCONFIRMED, 0.70, QUECLINK_ADAPTER, label, why, 5)


# --------------------------------------------------------------------------------------
# Branch 6 — NMEA 0183
# --------------------------------------------------------------------------------------

NMEA_ADAPTER = "none in-repo yet — target Opstrax.Telematics.Protocols.Nmea (rare as a primary uplink)"
NMEA_TALKERS = {ord("P"), ord("N"), ord("L"), ord("A"), ord("B")}


def try_nmea(b: bytes) -> Optional[Verdict]:
    if len(b) < 3 or b[0] != 0x24 or b[1] != 0x47 or b[2] not in NMEA_TALKERS:
        return None

    talker = b[:3].decode("ascii")
    why = ["b[0..3] == 24 47 %02X (%r) -> NMEA 0183 talker id (branch 6). Second byte is a "
           "letter, not '$', so this is NOT Meitrack." % (b[2], talker)]
    sentence = b[3:6].decode("ascii", "replace")
    kinds = {"RMC": "recommended minimum: lat/lng/speed/course/date",
             "GGA": "fix data: lat/lng, fix quality, satellites",
             "VTG": "course + ground speed", "GSA": "DOP + active satellites",
             "GSV": "satellites in view", "GLL": "geographic position"}
    why.append("Sentence id = %r%s." % (sentence, " -> %s" % kinds[sentence] if sentence in kinds else ""))

    star = b.rfind(b"*")
    if star == -1 or len(b) < star + 3:
        why.append("CONFIRM incomplete: no '*hh' checksum tail — capture through the CRLF.")
        return Verdict("NMEA 0183", UNCONFIRMED, 0.65, NMEA_ADAPTER, "$G" + chr(b[2]), why, 6)

    try:
        found = int(b[star + 1:star + 3].decode("ascii"), 16)
    except (UnicodeDecodeError, ValueError):
        why.append("CONFIRM FAILED: bytes after '*' are not 2 ASCII hex digits.")
        return Verdict("NMEA 0183", UNCONFIRMED, 0.40, NMEA_ADAPTER, "$G" + chr(b[2]), why, 6)

    calculated = xor_checksum(b[1:star])  # XOR of everything between '$' and '*', exclusive
    if calculated == found:
        why.append("CONFIRM PASSED: XOR of bytes between '$' and '*' == 0x%02X, matches '*%02X'."
                   % (found, found))
        return Verdict("NMEA 0183", CONFIRMED, 0.97, NMEA_ADAPTER, "$G" + chr(b[2]), why, 6)

    why.append("CONFIRM FAILED: NMEA XOR checksum mismatch (computed 0x%02X, sentence carries "
               "0x%02X)." % (calculated, found))
    return Verdict("NMEA 0183", UNCONFIRMED, 0.45, NMEA_ADAPTER, "$G" + chr(b[2]), why, 6)


# --------------------------------------------------------------------------------------
# Branch 7 — TK103 / Xexun (ASCII legacy, weakest signatures — tested last)
# --------------------------------------------------------------------------------------

TK103_ADAPTER = "none in-repo yet — target Opstrax.Telematics.Protocols.Tk103"


def try_tk103(b: bytes) -> Optional[Verdict]:
    lowered = b[:64].lower()

    if starts_with(b, b"("):
        why = ["b[0] == 28 ('(') -> TK103 parenthesised GPRS frame (branch 7, heuristic)."]
        sig = "("
        if b.rstrip().endswith(b")"):
            why.append("Frame is closed by ')' — consistent with TK103 framing.")
            inner = b.strip()[1:-1]
            has_validity = b"A" in inner[:40] or b"V" in inner[:40]
            if len(inner) >= 20 and has_validity:
                why.append("CONFIRM PASSED (weak): device-id + body present with an A/V GPS-valid "
                           "flag. TK103 firmwares vary — a full field parse is required before "
                           "trusting the decode.")
                return Verdict("TK103/Xexun", CONFIRMED, 0.75, TK103_ADAPTER, sig, why, 7)
            why.append("CONFIRM FAILED: no recognisable A/V GPS-validity flag in the body. "
                       "Downgrade to Unknown rather than guess.")
            return Verdict("TK103/Xexun", UNCONFIRMED, 0.40, TK103_ADAPTER, sig, why, 7)
        why.append("CONFIRM incomplete: no closing ')' — capture the full frame.")
        return Verdict("TK103/Xexun", UNCONFIRMED, 0.50, TK103_ADAPTER, sig, why, 7)

    if lowered.startswith(b"imei:") or b"imei:" in lowered:
        why = ["ASCII 'imei:' marker present -> TK103/Xexun line format (branch 7, heuristic)."]
        sig = "imei:"
        if b"gprmc" in lowered or b"tracker" in lowered or b",a," in lowered or b",v," in lowered:
            why.append("CONFIRM PASSED (weak): the line also carries a recognisable "
                       "tracker/GPRMC/validity field. Full field parse still required before "
                       "trusting the decode.")
            return Verdict("TK103/Xexun", CONFIRMED, 0.72, TK103_ADAPTER, sig, why, 7)
        why.append("CONFIRM FAILED: 'imei:' alone is not sufficient evidence — no tracker/GPRMC/"
                   "validity field found. Downgrade to Unknown rather than guess.")
        return Verdict("TK103/Xexun", UNCONFIRMED, 0.40, TK103_ADAPTER, sig, why, 7)

    return None


# --------------------------------------------------------------------------------------
# The decision tree — ORDER IS LOAD-BEARING
# --------------------------------------------------------------------------------------

BRANCHES: List[Tuple[str, Callable[[bytes], Optional[Verdict]]]] = [
    ("GT06/Concox", try_gt06),          # 1. binary, unambiguous
    ("Teltonika", try_teltonika),       # 2. binary, unambiguous
    ("JT/T808", try_jt808),             # 3. binary framing
    ("Meitrack", try_meitrack),         # 4. ASCII "$$" — before NMEA ("$G")
    ("Queclink", try_queclink),         # 5. ASCII "+RESP:"/"+ACK:"/"+BUFF:"
    ("NMEA 0183", try_nmea),            # 6. ASCII "$GP"/"$GN"/...
    ("TK103/Xexun", try_tk103),         # 7. ASCII heuristics — weakest, last
]


def fingerprint(data: bytes) -> Verdict:
    """Walk the decision tree in order; the FIRST branch whose signature matches wins."""
    if not data:
        return Verdict("Unknown", UNKNOWN, 0.0, "none", "-",
                       ["Empty capture — nothing to fingerprint."], None)

    for _name, probe in BRANCHES:
        verdict = probe(data)
        if verdict is not None:
            return verdict

    printable = sum(1 for c in data[:64] if c in _PRINTABLE)
    ratio = printable / float(min(len(data), 64))
    why = [
        "No branch signature matched (checked, in order: GT06 78 78/79 79; Teltonika "
        "00 00 00 00 / 00 0F; JT808 7E; Meitrack \"$$\"; Queclink \"+RESP:\"/\"+ACK:\"/\"+BUFF:\"; "
        "NMEA \"$G\"; TK103 \"(\"/\"imei:\").",
        "First bytes: %s" % hexdump(data, 16),
        "Payload looks %s (%.0f%% printable in the first 64 bytes)."
        % ("ASCII/text" if ratio > 0.85 else "binary", ratio * 100),
        "DO NOT GUESS. Archive the raw bytes, then re-capture a longer sample — some devices "
        "send a short login/keepalive first whose signature differs from their location frame.",
    ]
    return Verdict("Unknown", UNKNOWN, 0.0, "none — needs manufacturer protocol doc or a wider capture",
                   "-", why, None)


# --------------------------------------------------------------------------------------
# Output
# --------------------------------------------------------------------------------------

STATUS_LABEL = {
    CONFIRMED: "CONFIRMED  (signature matched + confirm step passed)",
    UNCONFIRMED: "UNCONFIRMED (signature matched, confirm step did NOT pass)",
    UNKNOWN: "UNKNOWN    (no signature matched)",
}


def render(v: Verdict, data: bytes) -> str:
    lines = [
        "=" * 78,
        "  OpsTrax telematics protocol fingerprint",
        "  (decision tree: docs/telematics/pt40/pt40-fingerprint.md)",
        "=" * 78,
        "",
        "  Captured bytes : %d" % len(data),
        "  Hex            : %s" % hexdump(data),
        "  ASCII          : %s" % ascii_preview(data),
        "",
        "  PROTOCOL       : %s" % v.protocol,
        "  STATUS         : %s" % STATUS_LABEL[v.status],
        "  CONFIDENCE     : %.2f" % v.confidence,
        "  BRANCH         : %s" % ("%d" % v.branch if v.branch else "— (fell through)"),
        "  SIGNATURE      : %s" % v.signature,
        "  ADAPTER        : %s" % v.adapter,
        "",
        "  RATIONALE",
    ]
    for i, r in enumerate(v.rationale, 1):
        lines.append("   %d. %s" % (i, r))
    lines.append("")
    if v.status != CONFIRMED:
        lines.append("  ⚠ Not confirmed — do NOT record this protocol as evidence. Per the tree, a")
        lines.append("    matched signature is only a candidate until its CONFIRM step passes.")
        lines.append("")
    lines.append("=" * 78)
    return "\n".join(lines)


# --------------------------------------------------------------------------------------
# Self-test — built-in vectors exercising each branch
# --------------------------------------------------------------------------------------

def _build_gt06_login() -> bytes:
    """Synthesize a well-formed GT06 login frame (valid length + CRC-ITU + 0D 0A).

    Length byte counts protocol-number .. CRC; CRC-ITU covers length .. serial.
    """
    inner = bytes([0x01]) + bytes.fromhex("0862464068456321") + bytes([0x00, 0x01])
    #             ^proto=login  ^8-byte BCD IMEI                 ^serial
    length = len(inner) + 2  # + 2-byte CRC
    prefix = bytes([length]) + inner
    crc = crc_itu(prefix)
    return b"\x78\x78" + prefix + bytes([crc >> 8, crc & 0xFF]) + b"\x0d\x0a"


def _build_nmea() -> bytes:
    payload = b"GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W"
    return b"$" + payload + b"*%02X\r\n" % xor_checksum(payload)


def _build_jt808() -> bytes:
    payload = bytes.fromhex("0200002C") + bytes.fromhex("013912344321") + bytes.fromhex("007D")
    return b"\x7e" + payload + bytes([xor_checksum(payload)]) + b"\x7e"


def _build_teltonika_imei() -> bytes:
    return b"\x00\x0f" + b"862464068456321"


def _build_meitrack() -> bytes:
    core = b"$$A21,862464068456321,AAA,35,22.5,114.0,000000,A,0,0,0"
    upto_star = core + b"*"
    return upto_star + b"%02X" % (sum(upto_star) & 0xFF) + b"\r\n"


def self_test() -> int:
    cases = [
        ("GT06 login (synthesized, valid CRC-ITU)", _build_gt06_login(), "GT06/Concox", CONFIRMED),
        ("Teltonika IMEI handshake", _build_teltonika_imei(), "Teltonika (IMEI handshake)", CONFIRMED),
        ("JT/T808 location", _build_jt808(), "JT/T808", CONFIRMED),
        ("Meitrack $$", _build_meitrack(), "Meitrack", CONFIRMED),
        ("Queclink +RESP", b"+RESP:GTFRI,060228,862464068456321,,,10,1,1,0.0,0,0.0,114.0,22.5,,,,,,,,,0102$", "Queclink", CONFIRMED),
        ("NMEA $GPRMC", _build_nmea(), "NMEA 0183", CONFIRMED),
        ("TK103 parens", b"(027044702680BR00250101A2233.0000N11400.0000E000.0000000000.0000000000L000000)", "TK103/Xexun", CONFIRMED),
        ("Garbage", bytes([0xDE, 0xAD, 0xBE, 0xEF, 0x99, 0x11]), "Unknown", UNKNOWN),
        ("GT06 sig w/ broken CRC", b"\x78\x78\x11\x01\x08\x62\x46\x40\x68\x45\x63\x21\x00\x01\xFF\xFF\x0d\x0a", "GT06/Concox", UNCONFIRMED),
    ]
    failures = 0
    for label, data, want_proto, want_status in cases:
        v = fingerprint(data)
        ok = v.protocol == want_proto and v.status == want_status
        if not ok:
            failures += 1
        print("[%s] %-38s -> %-28s %s"
              % ("PASS" if ok else "FAIL", label, v.protocol, v.status))
        if not ok:
            print("        expected %s / %s" % (want_proto, want_status))
    print("\n%d/%d vectors passed." % (len(cases) - failures, len(cases)))
    return 0 if failures == 0 else 1


# --------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------

EXIT_OK, EXIT_USAGE, EXIT_UNCONFIRMED, EXIT_UNKNOWN = 0, 1, 2, 3


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="Identify a telematics protocol from its first captured bytes "
                    "(read-only; implements docs/telematics/pt40/pt40-fingerprint.md).",
        epilog="Exit: 0 confirmed, 2 signature-only, 3 unknown, 1 bad input.",
    )
    parser.add_argument("hex_positional", nargs="?", metavar="HEX",
                        help="hex string of the captured bytes")
    parser.add_argument("--hex", dest="hex_flag", help="hex string (alternative to positional)")
    parser.add_argument("--file", dest="path",
                        help="capture file: raw binary, or text-of-hex (auto-detected). '-' = stdin")
    parser.add_argument("--json", action="store_true", help="machine-readable output")
    parser.add_argument("--self-test", action="store_true", help="run built-in vectors and exit")
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()

    sources = [s for s in (args.hex_positional, args.hex_flag, args.path) if s]
    if len(sources) != 1:
        parser.error("provide exactly one of: HEX positional, --hex, or --file")

    try:
        if args.path:
            data = read_capture(args.path)
        else:
            data = parse_hex(args.hex_positional or args.hex_flag)
    except (OSError, ValueError) as exc:
        print("error: %s" % exc, file=sys.stderr)
        return EXIT_USAGE

    verdict = fingerprint(data)

    if args.json:
        print(json.dumps({
            "protocol": verdict.protocol,
            "status": verdict.status,
            "confidence": round(verdict.confidence, 2),
            "branch": verdict.branch,
            "signature": verdict.signature,
            "adapter": verdict.adapter,
            "bytes": len(data),
            "hex": data.hex(),
            "rationale": verdict.rationale,
        }, indent=2))
    else:
        print(render(verdict, data))

    if verdict.status == CONFIRMED:
        return EXIT_OK
    if verdict.status == UNCONFIRMED:
        return EXIT_UNCONFIRMED
    return EXIT_UNKNOWN


if __name__ == "__main__":
    sys.exit(main())
