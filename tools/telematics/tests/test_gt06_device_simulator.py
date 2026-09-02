"""Tests for the GT06 device simulator.

The simulator's job is to be indistinguishable from a tracker on the wire, so what is asserted
here is the wire itself: real CRC-ITU checksums, the documented bit layout, lossless IMEI packing,
and safety rails that refuse to point synthetic traffic at a real fleet edge. The socket tests bind
loopback only and never leave the machine.
"""

from __future__ import annotations

from contextlib import redirect_stdout
import datetime as _dt
import io
from pathlib import Path
import socket
import sys
import threading
import unittest

TOOLS = Path(__file__).resolve().parents[1]
REPOSITORY = TOOLS.parents[1]
FIXTURES = REPOSITORY / "telematics" / "fixtures" / "gt06"
sys.path.insert(0, str(TOOLS))

import gt06_device_simulator as sim  # noqa: E402


class ChecksumTests(unittest.TestCase):
    def test_canonical_check_value(self):
        self.assertEqual(0x906E, sim.crc16_itu(b"123456789"))

    def test_every_committed_fixture_verifies(self):
        """The simulator's checksum must agree with the fixtures the decoder is tested against."""
        checked = 0
        for path in sorted(FIXTURES.glob("*.hex")):
            raw = bytes.fromhex("".join(c for c in path.read_text() if c in "0123456789abcdefABCDEF"))
            if len(raw) < 10 or raw[0:2] != b"\x78\x78":
                continue                       # 0x7979 framing and deliberate malformations
            packet_length = raw[2]
            crc_at = 3 + packet_length - 2
            if crc_at + 2 > len(raw):
                continue
            wire = int.from_bytes(raw[crc_at:crc_at + 2], "big")
            calc = sim.crc16_itu(raw[2:crc_at])
            expected_mismatch = path.name == "bad_crc.hex"
            self.assertEqual(wire != calc, expected_mismatch, f"{path.name} checksum")
            checked += 1
        self.assertGreater(checked, 10, "expected to check a meaningful number of fixtures")

    def test_corrupting_a_checksum_is_detectable_and_preserves_framing(self):
        frame = sim.login_frame("868120303337976")
        corrupted = sim.corrupt_checksum(frame)
        self.assertEqual(len(frame), len(corrupted))
        self.assertEqual(frame[0:4], corrupted[0:4])       # framing and protocol intact
        self.assertEqual(b"\x0d\x0a", corrupted[-2:])       # stop bits intact
        self.assertTrue(sim.parse_ack(frame)["crc_ok"])
        self.assertFalse(sim.parse_ack(corrupted)["crc_ok"])


class ImeiTests(unittest.TestCase):
    def test_packs_to_eight_bytes_with_one_pad_nibble(self):
        self.assertEqual("0868120303337976", sim.pack_imei("868120303337976").hex().upper())

    def test_leading_zero_imei_is_packed_losslessly(self):
        """The defect CERT-001 fixed lived on the decode side; the encoder must not reintroduce it."""
        for imei in ("012345678901234", "001234567890123", "000000000000001"):
            packed = sim.pack_imei(imei)
            nibbles = "".join(f"{b >> 4}{b & 0xF}" for b in packed)
            self.assertEqual(imei, nibbles[1:], f"{imei} did not survive packing")

    def test_rejects_a_non_numeric_identifier(self):
        with self.assertRaises(ValueError):
            sim.pack_imei("86812030333797X")

    def test_synthetic_identities_are_distinct_and_well_formed(self):
        ids = sim.synthetic_imeis("868120300000000", 500)
        self.assertEqual(500, len(set(ids)))
        self.assertTrue(all(len(i) == 15 and i.isdigit() for i in ids))

    def test_synthetic_base_must_be_fifteen_digits(self):
        for bad in ("123", "8681203000000000", "abcdefghijklmno"):
            with self.assertRaises(ValueError):
                sim.synthetic_imeis(bad, 1)


class FrameLayoutTests(unittest.TestCase):
    NOW = _dt.datetime(2024, 1, 15, 10, 20, 30)

    def _status_word(self, frame: bytes) -> int:
        # content starts at index 4; the course/status word is the last 2 bytes of the 18-byte block
        return int.from_bytes(frame[4 + 16:4 + 18], "big")

    def test_all_four_quadrants_set_the_documented_bits(self):
        cases = [
            (35.6762, 139.6503, True, False),   # north/east
            (40.7128, -74.0060, True, True),    # north/west
            (-33.8688, 151.2093, False, False), # south/east
            (-34.6037, -58.3816, False, True),  # south/west
        ]
        for lat, lng, north, west in cases:
            word = self._status_word(sim.location_frame(1, self.NOW, lat, lng))
            self.assertEqual(north, bool(word & sim.BIT_NORTH), f"lat {lat}")
            self.assertEqual(west, bool(word & sim.BIT_WEST), f"lng {lng}")
            self.assertTrue(word & sim.BIT_POSITIONED)

    def test_course_occupies_bits_zero_to_nine_only(self):
        for course in (0, 1, 217, 359):
            word = self._status_word(sim.location_frame(1, self.NOW, 38.0, -77.0, course=course))
            self.assertEqual(course, word & 0x03FF)

    def test_differential_flag_is_bit_thirteen(self):
        realtime = self._status_word(sim.location_frame(1, self.NOW, 38.0, -77.0, differential=False))
        differential = self._status_word(sim.location_frame(1, self.NOW, 38.0, -77.0, differential=True))
        self.assertFalse(realtime & sim.BIT_DIFFERENTIAL)
        self.assertTrue(differential & sim.BIT_DIFFERENTIAL)

    def test_terminal_info_bit_seven_marks_oil_electricity_CUT(self):
        self.assertFalse(sim.heartbeat_frame(1, oil_connected=True)[4] & 0x80)
        self.assertTrue(sim.heartbeat_frame(1, oil_connected=False)[4] & 0x80)

    def test_every_builder_emits_a_self_consistent_frame(self):
        frames = {
            "login": sim.login_frame("868120303337976"),
            "location": sim.location_frame(2, self.NOW, 38.0, -77.0),
            "heartbeat": sim.heartbeat_frame(3),
            "alarm": sim.alarm_frame(4, self.NOW, 38.0, -77.0, 0x01),
            "time": sim.time_request_frame(5),
        }
        for name, frame in frames.items():
            parsed = sim.parse_ack(frame)
            self.assertIsNotNone(parsed, name)
            self.assertTrue(parsed["crc_ok"], f"{name} checksum")
            self.assertEqual(len(frame), parsed["bytes"], name)
            self.assertEqual(b"\x0d\x0a", frame[-2:], name)

    def test_serial_must_fit_sixteen_bits(self):
        with self.assertRaises(ValueError):
            sim.build_frame(sim.PROTO_LOGIN, b"", 0x10000)


class SafetyTests(unittest.TestCase):
    def test_production_environment_is_refused(self):
        with self.assertRaises(ValueError):
            sim.validate_target("host.example", 5023, "production", set(), sim.SEND_ACK, True)

    def test_known_production_hosts_are_refused_even_when_allowlisted(self):
        for host in sim.KNOWN_PRODUCTION_HOSTS:
            with self.assertRaises(ValueError):
                sim.validate_target(host, 5023, "staging", {host}, sim.SEND_ACK, True)

    def test_non_loopback_requires_explicit_allowlisting_and_acknowledgement(self):
        with self.assertRaises(ValueError):
            sim.validate_target("edge.example", 5023, "staging", set(), sim.SEND_ACK, True)
        with self.assertRaises(ValueError):
            sim.validate_target("edge.example", 5023, "staging", {"edge.example"}, None, True)
        sim.validate_target("edge.example", 5023, "staging", {"edge.example"}, sim.SEND_ACK, True)

    def test_loopback_needs_no_acknowledgement(self):
        for host in ("127.0.0.1", "localhost", "::1"):
            sim.validate_target(host, 5023, "local", set(), None, True)

    def test_a_plan_only_run_never_validates_a_remote_target(self):
        """Without --send nothing is sent, so an unlisted host must not be rejected outright."""
        sim.validate_target("edge.example", 5023, "staging", set(), None, False)

    def test_malformed_hosts_and_ports_are_refused(self):
        for host in ("http://edge.example", "user@edge", "edge:5023", ""):
            with self.assertRaises(ValueError):
                sim.validate_target(host, 5023, "local", set(), None, True)
        for port in (0, 65536, -1):
            with self.assertRaises(ValueError):
                sim.validate_target("127.0.0.1", port, "local", set(), None, True)

    def test_default_invocation_opens_no_socket(self):
        """The zero-network default: a plan run must not touch the network at all."""
        original = socket.create_connection

        def explode(*args, **kwargs):
            raise AssertionError("a plan-only run opened a socket")

        socket.create_connection = explode
        try:
            with redirect_stdout(io.StringIO()):
                self.assertEqual(0, sim.main(["--scenario", "drive", "--devices", "2"]))
        finally:
            socket.create_connection = original


class SocketBehaviourTests(unittest.TestCase):
    """Loopback only. An echo-free listener stands in for a gateway."""

    def setUp(self):
        self.server = socket.socket()
        self.server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server.bind(("127.0.0.1", 0))
        self.server.listen(8)
        self.port = self.server.getsockname()[1]
        self.received: list[bytes] = []
        self.thread = threading.Thread(target=self._serve, daemon=True)
        self.thread.start()

    def _serve(self):
        try:
            conn, _ = self.server.accept()
        except OSError:
            return
        with conn:
            conn.settimeout(3.0)
            buffer = b""
            while True:
                try:
                    chunk = conn.recv(4096)
                except (socket.timeout, OSError):
                    break
                if not chunk:
                    break
                buffer += chunk
                self.received.append(chunk)
            self.collected = buffer

    def tearDown(self):
        try:
            self.server.close()
        except OSError:
            pass

    def test_a_login_arrives_intact_and_verifies_at_the_far_end(self):
        device = sim.SimulatedDevice("868120300000000", "127.0.0.1", self.port, timeout=2.0)
        device.connect()
        device.send(sim.login_frame(device.imei, 1))
        device.close()
        self.thread.join(timeout=4.0)

        arrived = b"".join(self.received)
        self.assertEqual(sim.login_frame("868120300000000", 1), arrived)
        self.assertTrue(sim.parse_ack(arrived)["crc_ok"])

    def test_chunked_send_delivers_identical_bytes(self):
        device = sim.SimulatedDevice("868120300000000", "127.0.0.1", self.port, timeout=2.0)
        device.connect()
        frame = sim.heartbeat_frame(7)
        device.send(frame, chunked=True)
        device.close()
        self.thread.join(timeout=6.0)

        self.assertEqual(frame, b"".join(self.received))
        self.assertGreater(len(self.received), 1, "chunked send should arrive in multiple reads")

    def test_power_cycle_resets_the_frame_counter(self):
        device = sim.SimulatedDevice("868120300000000", "127.0.0.1", self.port, timeout=2.0)
        device.connect()
        for _ in range(5):
            device.next_serial()
        self.assertEqual(6, device.serial)
        device.power_cycle()
        self.assertEqual(1, device.serial, "a power cycle must restart the counter at 1")

    def test_serial_wraps_at_sixteen_bits(self):
        device = sim.SimulatedDevice("868120300000000", "127.0.0.1", self.port, timeout=2.0)
        device.serial = 0xFFFF
        self.assertEqual(0xFFFF, device.next_serial())
        self.assertEqual(1, device.serial, "the counter wraps rather than overflowing the field")


if __name__ == "__main__":
    unittest.main()
