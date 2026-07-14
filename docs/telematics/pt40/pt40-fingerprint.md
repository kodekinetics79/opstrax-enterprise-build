# PT40 protocol fingerprint — deterministic decision tree

**Status of the PT40-Q under test:** IMEI `862464068456321`, serial `4C4000067803`.
The physical unit exists in the lab. **We do not have its manufacturer protocol
document and have not yet captured a byte from it, so its protocol is UNKNOWN and is
not claimed anywhere in this repo.** This document is the procedure that will *derive*
the protocol from the first bytes the device actually sends — it is not an assertion
about what those bytes will be.

> Do not write a `ProtocolName` for this device into `eld_devices`, the registry, or
> any config until step **Confirm** below passes against a real capture. A registry row
> is an identity record, never protocol evidence — see
> `docs/TELEMATICS_INTEGRATION.md` ("PT40 field commissioning gate").

---

## How to use this tree

1. Capture the **first frame** the device sends after it connects (raw bytes, before any
   transform). One frame is enough to fingerprint; keep the raw capture for archive.
2. Walk the branches **top to bottom, in order**. The order is deliberate: unambiguous
   binary framing signatures are tested before ASCII heuristics so a printable-looking
   binary frame cannot be misread as text. **The first branch whose signature matches
   wins** — stop there and run that branch's Confirm step.
3. A matched Signature is a *candidate*; the fingerprint is only accepted once the
   **Confirm** step passes. If Confirm fails, treat the frame as `Unknown` and re-capture
   — never fall through to a lower branch after a signature already matched.
4. `tools/telematics/fingerprint.py` implements this exact tree and prints the branch +
   rationale for a hex string or capture file. Use it as the reference decoder for this
   procedure.

Byte offsets below are 0-based. `b[0]` is the first captured byte.

---

## Branch order (summary)

| # | Protocol | Signature (first bytes) | Transport it usually rides |
|---|----------|-------------------------|----------------------------|
| 1 | GT06 / Concox / Jimi | `78 78` or `79 79` … `0D 0A` | Raw TCP |
| 2 | Teltonika Codec8 / 8E / 16 | `00 00 00 00` + len + codec `08`/`8E`/`10` (or `00 0F` IMEI handshake) | Raw TCP |
| 3 | JT/T808 (JT808) | `7E` … `7E` | Raw TCP |
| 4 | Meitrack | `24 24` (`"$$"`) | TCP / UDP, ASCII |
| 5 | Queclink | `2B` + `RESP:` / `ACK:` / `BUFF:` (`"+RESP:"`…) | TCP / UDP, ASCII |
| 6 | NMEA 0183 | `24 47` + `P`/`N`/`L` (`"$GP"`, `"$GN"`, …) | ASCII pass-through |
| 7 | TK103 / Xexun | `28` (`"("`) or ASCII `imei:` | TCP / UDP, ASCII |
| 8 | *(none matched)* | — | → `Unknown`, re-capture |

---

## 1. GT06 / Concox / Jimi family

- **Signature bytes:** `b[0..2] == 78 78` (standard packet, 1-byte length field) **or**
  `b[0..2] == 79 79` (extended packet, 2-byte length field). *(This is the family the
  PT40-Q is widely reported to belong to — but we still confirm from bytes, not from
  reputation.)*
- **Confirm:**
  - For `78 78`: `len = b[2]`; the frame is `78 78 | len(1) | protocol(1) | payload |
    serial(2) | crc(2) | 0D 0A`. Verify the two-byte stop `0D 0A` sits at offset
    `2 + 1 + len + 2` (i.e. `len` counts everything from the protocol byte through the
    CRC), and that **CRC-ITU (CRC16/X-25)** over `len … serial` equals `b[serial+2..+2]`.
  - For `79 79`: `len = (b[2] << 8) | b[3]` (2-byte big-endian length), same trailer/CRC
    logic shifted by one length byte.
  - The protocol-number byte selects the message type (`0x01` login carrying the 8-byte
    BCD IMEI, `0x12`/`0x22` location, `0x13` heartbeat/status, `0x16`/`0x26` alarm).
- **Adapter:** `Opstrax.Telematics.Protocols.Gt06.Gt06Adapter`
  (`Metadata.Name == "GT06"`, `AdapterVersion == "1.0.0"`). STATUS (verified 2026-07-14):
  this is a **real, implemented `IProtocolAdapter`** — `TryIdentify` (78 78 / 79 79 sync),
  `Decode` with CRC-ITU (CRC-16/X.25) framing, and login/heartbeat/location/status/alarm
  message decode into `CanonicalTelemetryEvent`, covered by 30 unit + fixture tests in
  `Opstrax.Telematics.Protocols.Tests`. So IF the PT40-Q's first frame fingerprints as
  GT06, the decoder is ready — the remaining work is not implementing the adapter but
  proving the fingerprint against a real captured frame (Confirm step) and deploying the
  gateway that runs it (see the onboarding runbook's root blockers).

## 2. Teltonika Codec8 / Codec8 Extended / Codec16

- **Signature bytes — two distinct first-contact shapes:**
  - **Data packet:** `b[0..4] == 00 00 00 00` (4-byte zero preamble), then
    `b[4..8]` = 4-byte big-endian **Data Field Length**, then **`b[8]` = Codec ID**:
    `08` = Codec8, `8E` = Codec8 Extended, `10` = Codec16. (`0C` Codec12, `0D` Codec13,
    `0F` Codec15 are the GPRS command/response codecs — recognise but route as commands.)
  - **IMEI handshake (the actual first packet a Teltonika device sends on connect):**
    `b[0..2] == 00 0F` (2-byte length = 15), followed by 15 ASCII IMEI digits. The server
    replies `01` to accept before any data packet arrives.
- **Confirm:** `b[9]` = number of records (data 1); the trailing byte-count "data 2" must
  equal it, and the final 4 bytes are **CRC-16/IBM (CRC16/ARC)** over `codec_id …
  data 2`. For the IMEI handshake, confirm exactly 15 printable digits after `00 0F`.
- **Adapter:** none in-repo yet. Target: `Opstrax.Telematics.Protocols.Teltonika`
  implementing `IProtocolAdapter` (codec-id dispatch, AVL record + IO element decode,
  `01`/CRC ack via `EncodeAck`).

## 3. JT/T808 (JT808 — GB Chinese national standard)

- **Signature bytes:** `b[0] == 7E` **and** the frame ends with `7E` (`0x7E` is the start
  *and* end flag). Body is byte-stuffed: `7E → 7D 02`, `7D → 7D 01`.
- **Confirm:** un-escape the body between the flags, then verify the 1-byte **XOR
  checksum** (XOR of every byte from message-id through the last body byte) equals the
  byte immediately before the closing `7E`. Header after the opening flag is
  `msg id(2) | body attributes(2) | [protocol version(1) — 2019 rev] | terminal phone
  BCD(6 or 10) | msg serial(2)`.
- **Adapter:** none in-repo yet. Target: `Opstrax.Telematics.Protocols.Jt808`
  (flag-scan + de-stuff framing, XOR-checksum guard, `0x0200` location decode,
  `0x8001` general-response ack).

## 4. Meitrack

- **Signature bytes:** `b[0..2] == 24 24` (ASCII `"$$"`). Frame:
  `$$ | flag(1) | length | , IMEI , command , … *checksum \r\n`.
- **Confirm:** frame ends with `2A` (`*`) + 2 ASCII hex checksum digits + `0D 0A`; the
  checksum is the **XOR/sum of all bytes from the first `$` up to and including the `*`**,
  rendered as 2 hex chars. The 3rd field is the IMEI. Disambiguates from NMEA because the
  **second** byte is `$` (`24`), not a talker letter.
- **Adapter:** none in-repo yet. Target: `Opstrax.Telematics.Protocols.Meitrack`
  (ASCII field split, `AAA` tracker-record decode, checksum guard).

## 5. Queclink (GL / GV / GB series)

- **Signature bytes:** `b[0] == 2B` (`"+"`) followed by an ASCII header token —
  `RESP:` (spontaneous report), `ACK:` (acknowledgement), or `BUFF:` (buffered/replayed
  report). So the first bytes read `"+RESP:"`, `"+ACK:"`, or `"+BUFF:"`.
- **Confirm:** comma-delimited ASCII record terminated by `$` (`24`), optionally followed
  by `\r\n`. The token after the header (e.g. `GTFRI`, `GTGEO`, `GTHBD`) names the report;
  the last comma-field before `$` is a message count/serial used for the `+SACK` ack.
- **Adapter:** none in-repo yet. Target: `Opstrax.Telematics.Protocols.Queclink`
  (`+RESP`/`+BUFF`/`+ACK` dispatch, `$`-terminated field split, `+SACK` encode).

## 6. NMEA 0183

- **Signature bytes:** `b[0] == 24` (`"$"`) **and** `b[1] == 47` (`"G"`) with `b[2]` in
  `{50 'P', 4E 'N', 4C 'L', 41 'A', 42 'B'}` → talker id (`$GP`, `$GN`, `$GL`, `$GA`,
  `$GB`), immediately followed by a 3-letter sentence id (`RMC`, `GGA`, `VTG`, …).
- **Confirm:** sentence ends `*hh\r\n` where `hh` is the 2-hex-digit **XOR of every byte
  between `$` and `*`** (exclusive). `$GPRMC`/`$GNRMC` carry lat/lng/speed/course/date;
  `$GPGGA` carries fix quality + satellites. Distinguished from Meitrack by `b[1]` being a
  letter, not a second `$`.
- **Adapter:** none in-repo yet (raw NMEA is rare for a tracker's *primary* uplink — it
  usually appears wrapped inside a vendor frame). Target:
  `Opstrax.Telematics.Protocols.Nmea` if a device is confirmed to emit bare sentences.

## 7. TK103 / Xexun (ASCII legacy)

- **Signature bytes (heuristic, lowest confidence — test last):**
  - TK103 GPRS: `b[0] == 28` (`"("`) — e.g.
    `(027044702680BR00250101A…)` wrapped in parentheses.
  - Xexun / TK103 alt: leading ASCII `imei:` (`69 6D 65 69 3A`) — e.g.
    `imei:359586015829802,tracker,…` or an SMS-style line embedding a bare `GPRMC`.
- **Confirm:** these firmwares are inconsistent, so require a full field parse to succeed
  (recognisable device-id field + `A`/`V` GPS-valid flag + parseable lat/lng) before
  accepting. If a full parse does not succeed, downgrade to `Unknown` rather than guess.
- **Adapter:** none in-repo yet. Target: `Opstrax.Telematics.Protocols.Tk103`
  (paren-frame and `imei:` line variants, `A`/`V` validity gate).

## 8. No branch matched → `Unknown`

If none of 1–7 match, do **not** guess. Emit `MessageType.Unknown`, archive the raw
bytes, and re-capture a longer sample. A device that repeatedly fingerprints `Unknown`
needs its manufacturer protocol doc or a wider capture (some devices send a short
login/keepalive first whose signature differs from their location frame).

---

## Disambiguation notes (why the order is what it is)

- **`$` collisions:** three protocols open with `0x24`. Resolve by the *second* byte:
  `24 24` → Meitrack (branch 4); `24 47` → NMEA (branch 6). A lone `$` that is neither is
  not fingerprinted as either.
- **Binary before ASCII:** GT06 (`78 78`) and Teltonika (`00 00 00 00`) are tested first
  because their fixed binary preambles cannot occur at the start of a well-formed frame in
  any of the ASCII protocols, so an early binary match is safe and unambiguous.
- **CRC/checksum is part of identity, not just integrity:** GT06 (CRC-ITU), Teltonika
  (CRC16/IBM), JT808 (XOR), Meitrack/NMEA (XOR hex) each use a *different* trailer scheme.
  A passing checksum in the branch's own scheme is the strongest single confirmation that
  the fingerprint is correct — the `Confirm` step is not optional.

## Once a protocol is confirmed for the PT40-Q

1. Record the confirmed `ProtocolName` + `ProtocolVersion` as **evidence** (link the raw
   capture) — see step 4 and step 18 of `pt40-onboarding-runbook.md`.
2. Ensure the matching `IProtocolAdapter` exists and its `TryIdentify` returns
   `ProtocolMatch.Match(...)` for the captured bytes (highest-confidence adapter wins
   arbitration).
3. Only then does the gateway decode → normalize → forward to
   `POST /api/telemetry/gps-ingest`, and `CanonicalTelemetryEvent.ProtocolName` /
   `AdapterName` carry the confirmed value downstream.
