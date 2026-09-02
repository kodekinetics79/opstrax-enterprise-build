#!/usr/bin/env python3
"""Generate the GT06 hemisphere/positioning-mode fixtures from the PROTOCOL DOCUMENT.

Run:  python3 generate_quadrant_fixtures.py

Every value below is written from the public GT06/GT06N "Course, Status" bit table, NOT read
back from Gt06Adapter. That independence is the point: the decoder previously read bit 10 as
longitude and bit 11 as latitude — exactly backwards — and the pre-existing fixture set could
not detect it, because every GPS fixture happened to carry those two bits with the SAME value
(both set for the Dallas/London frames, both clear for Sydney). A swap is invisible when the
two bits agree, so the missing coverage was precisely the mixed quadrants: North/East and
South/West.

Course, Status word (16 bits, big-endian on the wire). The vendor document tabulates it as two
bytes; BYTE1 bit N of that table is bit (N+8) of the word:

    bits 0-9   course over ground, 0..359 degrees
    bit 10     latitude   : 1 = North,          0 = South            (BYTE1 bit2)
    bit 11     longitude  : 1 = West,           0 = East             (BYTE1 bit3)
    bit 12     positioned : 1 = GPS positioned, 0 = not positioned   (BYTE1 bit4)
    bit 13     mode       : 0 = real-time GPS,  1 = differential     (BYTE1 bit5)

Cross-checks used, both cited in README.md:
  * Traccar Gt06ProtocolDecoder: "if (!BitUtil.check(flags, 10)) latitude = -latitude;"
    and "if (BitUtil.check(flags, 11)) longitude = -longitude;".
  * The vendor document's worked example 0x154C, annotated "Bit5=0 -> real time GPS,
    Bit4=1 -> GPS has been positioned". Decoding 0x154C with the table above gives
    course 332, North, East, positioned, real-time — a self-consistent Chinese fix.
"""

BIT_NORTH = 1 << 10
BIT_WEST = 1 << 11
BIT_POSITIONED = 1 << 12
BIT_DIFFERENTIAL = 1 << 13


def crc16_itu(data: bytes) -> int:
    """CRC-ITU / CRC-16/X.25: reflected poly 0x8408, init 0xFFFF, xorout 0xFFFF."""
    crc = 0xFFFF
    for byte in data:
        crc ^= byte
        for _ in range(8):
            crc = (crc >> 1) ^ 0x8408 if crc & 1 else crc >> 1
    return ~crc & 0xFFFF


def course_status(course_deg: int, north: bool, west: bool,
                  positioned: bool = True, differential: bool = False) -> int:
    assert 0 <= course_deg < 1024
    word = course_deg
    if north:
        word |= BIT_NORTH
    if west:
        word |= BIT_WEST
    if positioned:
        word |= BIT_POSITIONED
    if differential:
        word |= BIT_DIFFERENTIAL
    return word


def gps_frame(protocol: int, serial: int, when, satellites: int,
              lat_deg: float, lng_deg: float, speed_kph: int, status_word: int) -> bytes:
    """Build one 0x7878 GPS frame. Coordinates are MAGNITUDES; sign lives in the status word."""
    year, month, day, hour, minute, second = when
    info = bytes([year - 2000, month, day, hour, minute, second, satellites & 0x0F])
    info += round(abs(lat_deg) * 1_800_000).to_bytes(4, "big")
    info += round(abs(lng_deg) * 1_800_000).to_bytes(4, "big")
    info += bytes([speed_kph])
    info += status_word.to_bytes(2, "big")
    # Trailing LBS block, same shape the other fixtures carry (MCC 460 / MNC 1 / LAC / CellId).
    info += bytes.fromhex("01CC0101550009C6")

    packet_length = 1 + len(info) + 2 + 2          # protocol + info + serial + crc
    crc_region = bytes([packet_length, protocol]) + info + serial.to_bytes(2, "big")
    crc = crc16_itu(crc_region)
    return b"\x78\x78" + crc_region + crc.to_bytes(2, "big") + b"\x0d\x0a"


FIX = (2024, 1, 15, 10, 20, 30)

FIXTURES = {
    # ── The four quadrants. Only these two are new information: the pre-existing set already
    #    covered North/West (Dallas) and South/East (Sydney), where bits 10 and 11 agree.
    "quadrant_north_east.hex": gps_frame(
        0x12, 0x0101, FIX, 9, 35.6762, 139.6503, 42,           # Tokyo
        course_status(45, north=True, west=False)),
    "quadrant_north_west.hex": gps_frame(
        0x12, 0x0102, FIX, 9, 40.7128, 74.0060, 42,            # New York
        course_status(135, north=True, west=True)),
    "quadrant_south_east.hex": gps_frame(
        0x12, 0x0103, FIX, 9, 33.8688, 151.2093, 42,           # Sydney
        course_status(225, north=False, west=False)),
    "quadrant_south_west.hex": gps_frame(
        0x12, 0x0104, FIX, 9, 34.6037, 58.3816, 42,            # Buenos Aires
        course_status(315, north=False, west=True)),

    # ── Positioning mode, both polarities of bit 13, otherwise identical frames. ──
    "positioning_realtime.hex": gps_frame(
        0x12, 0x0201, FIX, 9, 35.6762, 139.6503, 42,
        course_status(45, north=True, west=False, differential=False)),
    "positioning_differential.hex": gps_frame(
        0x12, 0x0202, FIX, 9, 35.6762, 139.6503, 42,
        course_status(45, north=True, west=False, differential=True)),
}

if __name__ == "__main__":
    for name, frame in FIXTURES.items():
        with open(name, "w", encoding="ascii") as handle:
            handle.write(frame.hex().upper() + "\n")
        print(f"{name:34s} {frame.hex().upper()}")
