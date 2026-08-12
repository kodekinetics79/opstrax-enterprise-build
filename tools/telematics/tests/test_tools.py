from __future__ import annotations

import argparse
from contextlib import redirect_stderr, redirect_stdout
import io
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock


TOOLS = Path(__file__).resolve().parents[1]
REPOSITORY = TOOLS.parents[1]
FIXTURES = REPOSITORY / "telematics" / "fixtures" / "gt06"
sys.path.insert(0, str(TOOLS))

import capture_listener  # noqa: E402
import fingerprint  # noqa: E402
import public_replay  # noqa: E402


class FingerprintTests(unittest.TestCase):
    def test_builtin_protocol_vectors_all_pass(self) -> None:
        with redirect_stdout(io.StringIO()):
            self.assertEqual(fingerprint.self_test(), 0)

    def test_committed_login_fixture_is_confirmed_gt06(self) -> None:
        payload = fingerprint.read_capture(str(FIXTURES / "login.hex"))
        verdict = fingerprint.fingerprint(payload)
        self.assertEqual(verdict.protocol, "GT06/Concox")
        self.assertEqual(verdict.status, fingerprint.CONFIRMED)

    def test_capture_input_is_capped_at_one_mebibyte(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            oversized = Path(directory) / "oversized.bin"
            oversized.write_bytes(b"x" * (fingerprint.MAX_CAPTURE_BYTES + 1))
            with self.assertRaisesRegex(ValueError, "1 MiB"):
                fingerprint.read_capture(str(oversized))


class CaptureListenerTests(unittest.TestCase):
    def test_valid_login_ack_matches_committed_fixture(self) -> None:
        login = fingerprint.read_capture(str(FIXTURES / "login.hex"))
        expected = fingerprint.read_capture(str(FIXTURES / "login_ack.hex"))
        self.assertEqual(capture_listener.try_gt06_ack(login), expected)

    def test_crc_invalid_frame_is_never_acknowledged(self) -> None:
        bad_crc = fingerprint.read_capture(str(FIXTURES / "bad_crc.hex"))
        self.assertIsNone(capture_listener.try_gt06_ack(bad_crc))

    def test_capture_path_cannot_escape_ignored_capture_root(self) -> None:
        with self.assertRaisesRegex(ValueError, "must stay under"):
            capture_listener.resolve_capture_path("../outside.hex")

    def test_capture_listener_refuses_production(self) -> None:
        cfg = argparse.Namespace(
            environment="production", host="127.0.0.1", port=5023, idle_timeout=5,
            public_staging=False, public_bind_ack="", gt06_ack=False,
            confirmed_protocol=None, active_ack="", out="test.hex",
        )
        with self.assertRaisesRegex(ValueError, "refuses production"):
            capture_listener.validate_config(cfg)

    def test_non_loopback_bind_needs_all_staging_controls(self) -> None:
        cfg = argparse.Namespace(
            environment="staging", host="0.0.0.0", port=5023, idle_timeout=5,
            public_staging=True, public_bind_ack="wrong", gt06_ack=False,
            confirmed_protocol=None, active_ack="", out="test.hex",
        )
        with self.assertRaisesRegex(ValueError, "non-loopback"):
            capture_listener.validate_config(cfg)

    def test_active_ack_needs_confirmed_protocol_and_acknowledgement(self) -> None:
        cfg = argparse.Namespace(
            environment="local", host="127.0.0.1", port=5023, idle_timeout=5,
            public_staging=False, public_bind_ack="", gt06_ack=True,
            confirmed_protocol="GT06", active_ack="wrong", out="test.hex",
        )
        with self.assertRaisesRegex(ValueError, "GT06 replies"):
            capture_listener.validate_config(cfg)

    def test_capture_file_is_created_mode_0600(self) -> None:
        relative = Path(f"unit-{os.getpid()}") / "capture.hex"
        path = capture_listener.resolve_capture_path(str(relative))
        capture_listener.prepare_capture_file(path)
        capture_listener.append_capture(path, "7878")
        try:
            self.assertEqual(path.stat().st_mode & 0o777, 0o600)
            self.assertEqual(path.read_text(encoding="utf-8"), "7878\n")
        finally:
            path.unlink(missing_ok=True)
            path.parent.rmdir()

    def test_irregular_chunks_never_persist_beyond_exact_connection_cap(self) -> None:
        total = 0
        persisted = bytearray()
        dropped = 0
        for chunk in [b"a" * 4095] * 257:
            recorded, total, newly_dropped = capture_listener.bounded_capture_chunk(chunk, total)
            persisted.extend(recorded)
            dropped += newly_dropped
        self.assertEqual(total, capture_listener.MAX_CONN_BYTES)
        self.assertEqual(len(persisted), capture_listener.MAX_CONN_BYTES)
        self.assertGreater(dropped, 0)


class PublicReplayTests(unittest.TestCase):
    def test_replay_refuses_each_known_production_host(self) -> None:
        for host in public_replay.KNOWN_PRODUCTION_HOSTS:
            with self.subTest(host=host), self.assertRaisesRegex(ValueError, "production host"):
                public_replay.validate_target(host, 5023, "staging", {host}, set(public_replay.KNOWN_PRODUCTION_HOSTS))

    def test_replay_requires_exact_staging_allowlist_membership(self) -> None:
        with self.assertRaisesRegex(ValueError, "explicitly listed"):
            public_replay.validate_target(
                "gateway-staging.example.test", 5023, "staging",
                {"other.example.test"}, set(public_replay.KNOWN_PRODUCTION_HOSTS),
            )
        public_replay.validate_target(
            "gateway-staging.example.test", 5023, "staging",
            {"gateway-staging.example.test"}, set(public_replay.KNOWN_PRODUCTION_HOSTS),
        )

    def test_replay_fixture_cannot_escape_committed_fixture_root(self) -> None:
        with self.assertRaisesRegex(ValueError, "must stay under"):
            public_replay.resolve_fixture("../../../tools/telematics/fingerprint.py")

    def test_replay_rejects_existing_but_untracked_fixture(self) -> None:
        with mock.patch.object(public_replay.subprocess, "run", return_value=mock.Mock(returncode=1)) as git_check:
            with self.assertRaisesRegex(ValueError, "not tracked by git"):
                public_replay.resolve_fixture("login.hex")
        git_check.assert_called_once()
        self.assertNotIn("shell", git_check.call_args.kwargs)

    def test_dry_run_performs_zero_network_calls(self) -> None:
        stdout = io.StringIO()
        with mock.patch.object(public_replay, "replay_once") as replay, redirect_stdout(stdout):
            status = public_replay.main([
                "--fixture", "login.hex",
                "--host", "gateway-staging.example.test",
                "--port", "5023",
                "--environment", "staging",
                "--allow-host", "gateway-staging.example.test",
                "--dry-run",
            ])
        self.assertEqual(status, 0)
        replay.assert_not_called()
        self.assertIn('"networkCalls": 0', stdout.getvalue())

    def test_send_mode_calls_one_shot_replay_once(self) -> None:
        stdout = io.StringIO()
        with mock.patch.object(public_replay, "replay_once", return_value=b"ack") as replay, redirect_stdout(stdout):
            status = public_replay.main([
                "--fixture", "login.hex",
                "--host", "gateway-staging.example.test",
                "--port", "5023",
                "--environment", "staging",
                "--allow-host", "gateway-staging.example.test",
                "--send",
                "--send-ack", public_replay.SEND_ACK,
            ])
        self.assertEqual(status, 0)
        replay.assert_called_once()
        self.assertIn('"networkCalls": 1', stdout.getvalue())
        self.assertNotIn("ack", stdout.getvalue())

    def test_unconfirmed_fixture_needs_both_negative_controls(self) -> None:
        stderr = io.StringIO()
        with redirect_stderr(stderr):
            status = public_replay.main([
                "--fixture", "bad_crc.hex",
                "--host", "gateway-staging.example.test",
                "--port", "5023",
                "--environment", "staging",
                "--allow-host", "gateway-staging.example.test",
                "--dry-run",
            ])
        self.assertEqual(status, 2)
        self.assertIn("not protocol-confirmed", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
