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
import certification_harness  # noqa: E402


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


class CertificationHarnessTests(unittest.TestCase):
    @staticmethod
    def large_credentials() -> list[certification_harness.Credential]:
        return [
            certification_harness.Credential(
                f"{branch}-DEV-{ordinal:04d}",
                f"api-{branch}-{ordinal:04d}",
                f"hmac-{branch}-{ordinal:04d}",
            )
            for branch in certification_harness.BRANCH_CENTERS
            for ordinal in range(1, certification_harness.DEVICES_PER_BRANCH + 1)
        ]

    def credential_file(self, directory: str, rows: list[tuple[str, str, str]]) -> Path:
        path = Path(directory) / "credentials.csv"
        path.write_text(
            "deviceSerial,apiKey,hmacSecret\n" +
            "".join(f"{serial},{api_key},{secret}\n" for serial, api_key, secret in rows),
            encoding="utf-8",
        )
        path.chmod(0o600)
        return path

    def test_signature_matches_canonical_contract(self) -> None:
        body = '{"lat":35.1,"lng":-80.2}'
        canonical = "POST\n/api/telemetry/ingest\n1700000000\nnonce-1\n" + certification_harness.sha256_hex(body)
        expected = __import__("hmac").new(
            b"device-secret", canonical.encode("utf-8"), __import__("hashlib").sha256,
        ).hexdigest()
        self.assertEqual(
            certification_harness.compute_signature(
                "device-secret", "POST", "/api/telemetry/ingest", "1700000000", "nonce-1", body,
            ),
            expected,
        )

    def test_credentials_must_be_mode_0600(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = self.credential_file(directory, [("CLHQ-DEV-0001", "key", "secret")])
            path.chmod(0o644)
            with self.assertRaisesRegex(ValueError, "group/world"):
                certification_harness.load_credentials([str(path)])

    def test_credentials_are_unique_and_header_is_exact(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = self.credential_file(directory, [
                ("CLHQ-DEV-0001", "key-1", "secret-1"),
                ("CLHQ-DEV-0001", "key-2", "secret-2"),
            ])
            with self.assertRaisesRegex(ValueError, "duplicate device serial"):
                certification_harness.load_credentials([str(path)])

    def test_plan_is_deterministic_and_contains_no_credentials(self) -> None:
        credentials = self.large_credentials()
        observed = certification_harness.datetime(2026, 8, 27, 5, 15, tzinfo=certification_harness.timezone.utc)
        first = certification_harness.build_scenarios(credentials, "RUN-1", observed)
        second = certification_harness.build_scenarios(credentials, "RUN-1", observed)
        self.assertEqual(first, second)
        public = __import__("json").dumps([scenario.public() for scenario in first])
        self.assertNotIn("api-CLHQ-0001", public)
        self.assertNotIn("hmac-CLHQ-0001", public)
        self.assertEqual(len(first), 7_642)
        self.assertEqual(sum(row.interface == "native" for row in first), 7_632)
        self.assertEqual(sum(row.interface == "diagnostic-native" for row in first), 10)
        by_name = {scenario.name: scenario for scenario in first}
        original = by_name["idempotency-original"]
        identical = by_name["idempotency-identical-fresh-nonce"]
        conflict = by_name["idempotency-conflict-fresh-nonce"]
        self.assertEqual(original.body, identical.body)
        self.assertNotEqual(original.nonce, identical.nonce)
        self.assertEqual(identical.expected_status, (200,))
        self.assertEqual(identical.expected_mutation, "none")
        self.assertEqual(
            __import__("json").loads(original.body)["clientGeneratedId"],
            __import__("json").loads(conflict.body)["clientGeneratedId"],
        )
        self.assertNotEqual(original.body, conflict.body)
        self.assertEqual(conflict.expected_status, (409,))

    def test_exact_per_branch_cohort_plan_and_never_connected_invariant(self) -> None:
        credentials = self.large_credentials()
        grouped = certification_harness.validate_large_fleet_credentials(credentials)
        counts = {name: 0 for name in certification_harness.EXPECTED_COHORT_TOTALS}
        for branch_rows in grouped.values():
            branch_counts = {name: 0 for name in counts}
            for credential in branch_rows:
                cohort = certification_harness._cohort(certification_harness._device_number(credential.serial))
                counts[cohort] += 1
                branch_counts[cohort] += 1
            self.assertEqual(branch_counts, {
                "normal": 140, "delayed": 20, "stale": 15, "offline": 10,
                "reconnect": 5, "geofence": 5, "odometer": 3,
                "critical-j1939": 2, "never-connected": 20,
            })
        self.assertEqual(counts, certification_harness.EXPECTED_COHORT_TOTALS)
        scenarios = certification_harness.build_scenarios(
            credentials, "RUN-COHORTS",
            certification_harness.datetime(2026, 8, 27, tzinfo=certification_harness.timezone.utc),
        )
        never_serials = {
            credential.serial for credential in credentials
            if certification_harness._cohort(certification_harness._device_number(credential.serial)) == "never-connected"
        }
        self.assertEqual(len(never_serials), 100)
        self.assertTrue(never_serials.isdisjoint({scenario.serial for scenario in scenarios}))

    def test_phase_schedule_keeps_non_offline_devices_online_and_final_live_cohorts_fresh(self) -> None:
        scenarios = certification_harness.build_scenarios(
            self.large_credentials(), "RUN-PHASES",
            certification_harness.datetime(2026, 8, 27, tzinfo=certification_harness.timezone.utc),
        )
        offsets = lambda serial, interface="native": [
            row.send_offset_seconds for row in scenarios
            if row.serial == serial and row.interface == interface and row.cohort != "control"
        ]
        self.assertEqual(max(offsets("CLHQ-DEV-0001")), certification_harness.RECONNECT_SECONDS)
        self.assertIn(2 * 60, offsets("CLHQ-DEV-0191"))
        self.assertEqual(max(offsets("CLHQ-DEV-0191")), certification_harness.RECONNECT_SECONDS)
        self.assertEqual(max(offsets("CLHQ-DEV-0196")), certification_harness.RECONNECT_SECONDS)
        self.assertEqual(offsets("CLHQ-DEV-0199"), [2 * 60, certification_harness.RECONNECT_SECONDS])
        self.assertEqual(offsets("CLHQ-DEV-0199", "diagnostic-native"), [15 * 60])
        self.assertEqual(max(offsets("CLHQ-DEV-0141")), 15 * 60)
        self.assertEqual(max(offsets("CLHQ-DEV-0161")), 15 * 60)
        self.assertEqual(offsets("CLHQ-DEV-0176"), [0])
        self.assertEqual(max(offsets("CLHQ-DEV-0186")), certification_harness.RECONNECT_SECONDS)

    def test_public_manifest_has_exact_non_secret_oracle_totals(self) -> None:
        credentials = self.large_credentials()
        observed = certification_harness.datetime(2026, 8, 27, 5, 15, tzinfo=certification_harness.timezone.utc)
        scenarios = certification_harness.build_scenarios(credentials, "RUN-MANIFEST", observed)
        plan = certification_harness.build_public_plan(
            credentials, scenarios, "RUN-MANIFEST", "staging.example.test", False,
        )
        self.assertEqual(plan["networkCalls"], 0)
        self.assertEqual(plan["expectedInventory"], {"devices": 1100, "installed": 1000, "neverConnected": 100})
        self.assertEqual(plan["cohortPerBranch"], {
            "normal": 140, "delayed": 20, "stale": 15, "offline": 10,
            "reconnect": 5, "geofence": 5, "odometer": 3,
            "critical-j1939": 2, "never-connected": 20,
        })
        self.assertEqual(plan["cohortTotals"], certification_harness.EXPECTED_COHORT_TOTALS)
        self.assertEqual(plan["interfaceEventTotals"], {"native": 7632, "diagnostic-native": 10})
        self.assertEqual(plan["eventTotals"], {
            "cohortGps": 7620,
            "positiveControlGps": 3,
            "idempotentNoOpSuccess": 1,
            "validDiagnostics": 10,
            "rejectedControls": 8,
            "negativeOrNoMutationControls": 9,
            "allPlannedAttempts": 7642,
        })
        self.assertEqual(plan["expectedChromeTotals"]["onlineAfterReconnect"], 950)
        encoded = __import__("json").dumps(plan)
        self.assertNotIn("api-CLHQ-0001", encoded)
        self.assertNotIn("hmac-CLHQ-0001", encoded)

    def test_j1939_fixture_derives_critical_without_client_severity(self) -> None:
        body = __import__("json").loads(certification_harness._diagnostic_body(
            "CLHQ-DEV-0199",
            certification_harness.datetime(2026, 8, 27, tzinfo=certification_harness.timezone.utc),
        ))
        self.assertEqual(body["protocol"], "J1939")
        self.assertEqual(body["pgn"], 65226)
        self.assertEqual(body["lampStatus"]["redStop"], "On")
        self.assertNotIn("severity", body)

    def test_large_fleet_rejects_partial_or_misaligned_credentials(self) -> None:
        with self.assertRaisesRegex(ValueError, "exactly 1100"):
            certification_harness.validate_large_fleet_credentials(self.large_credentials()[:-1])
        malformed = self.large_credentials()
        malformed[-1] = certification_harness.Credential("WESTHUB-DEV-9999", "api", "hmac")
        with self.assertRaisesRegex(ValueError, "0001..0220"):
            certification_harness.validate_large_fleet_credentials(malformed)

    def test_target_refuses_production_and_requires_exact_allowlist(self) -> None:
        with self.assertRaisesRegex(ValueError, "production"):
            certification_harness.validate_target(
                "https://osptrax-fleet-management.onrender.com",
                "osptrax-fleet-management.onrender.com", "staging",
            )
        with self.assertRaisesRegex(ValueError, "exactly match"):
            certification_harness.validate_target(
                "https://opstrax-staging-api.onrender.com", "other.example", "staging",
            )

    def test_default_plan_makes_zero_network_calls(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = self.credential_file(directory, [
                (credential.serial, credential.api_key, credential.hmac_secret)
                for credential in self.large_credentials()
            ])
            stdout = io.StringIO()
            with mock.patch.object(certification_harness.request, "urlopen") as urlopen, redirect_stdout(stdout):
                status = certification_harness.main([
                    "--credentials", str(path), "--run-id", "RUN-DRY",
                    "--observed-at", "2026-08-27T05:15:00Z",
                ])
            self.assertEqual(status, 0)
            urlopen.assert_not_called()
            self.assertIn('"networkCalls": 0', stdout.getvalue())
            self.assertNotIn("api-CLHQ-0001", stdout.getvalue())
            self.assertNotIn("hmac-CLHQ-0001", stdout.getvalue())

    def test_execute_requires_ack_and_exact_sha(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = self.credential_file(directory, [
                (credential.serial, credential.api_key, credential.hmac_secret)
                for credential in self.large_credentials()
            ])
            stderr = io.StringIO()
            with redirect_stderr(stderr):
                status = certification_harness.main([
                    "--credentials", str(path), "--run-id", "RUN-EXEC", "--execute",
                ])
            self.assertEqual(status, 2)
            self.assertIn("execute mode requires", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
