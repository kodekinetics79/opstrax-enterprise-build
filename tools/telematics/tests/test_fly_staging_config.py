from pathlib import Path
import re
import unittest


REPO_ROOT = Path(__file__).resolve().parents[3]
CONFIG_PATH = REPO_ROOT / "telematics" / "fly.staging-certification.toml"


class FlyStagingCertificationContractTests(unittest.TestCase):
    def test_manifest_resolves_existing_telematics_dockerfile(self):
        config = CONFIG_PATH.read_text(encoding="utf-8")
        match = re.search(r'^\s*dockerfile\s*=\s*"([^"]+)"\s*$', config, re.MULTILINE)
        self.assertIsNotNone(match, "manifest must declare one Dockerfile")

        resolved = (CONFIG_PATH.parent / match.group(1)).resolve()
        expected = (REPO_ROOT / "telematics" / "Dockerfile").resolve()
        self.assertEqual(resolved, expected)
        self.assertTrue(resolved.is_file(), "resolved Fly Dockerfile must exist")

        dockerfile = resolved.read_text(encoding="utf-8")
        self.assertRegex(
            dockerfile,
            r"(?m)^COPY telematics/src/",
            "Dockerfile must continue to use repository-root build context",
        )

    def test_manifest_retains_isolated_fail_closed_lane(self):
        config = CONFIG_PATH.read_text(encoding="utf-8")

        self.assertRegex(config, r'(?m)^app\s*=\s*"opstrax-telematics-staging-cert"$')
        self.assertRegex(config, r'(?m)^\s*Gateway__ListenPort\s*=\s*"5023"$')
        self.assertRegex(config, r'(?m)^\s*Gateway__Edge__Egress\s*=\s*"Https"$')
        self.assertRegex(
            config,
            r'(?m)^\s*Gateway__Edge__Forward__BaseUrl\s*=\s*"https://opstrax-staging-api\.onrender\.com"$',
        )
        self.assertRegex(
            config,
            r'(?m)^\s*Gateway__Edge__Forward__GatewayId\s*=\s*"g1b-staging-iad-1"$',
        )
        self.assertRegex(
            config,
            r'(?m)^\s*Gateway__Edge__Allowlist__Path\s*=\s*"/var/lib/opstrax-gateway/imei-allowlist\.txt"$',
        )
        self.assertRegex(
            config,
            r'(?m)^\s*Gateway__Edge__Outbox__Path\s*=\s*"/var/lib/opstrax-gateway/outbox"$',
        )
        self.assertNotRegex(config, r"Gateway__Edge__Allowlist__Inline|Gateway__Edge__Allowlist__Imeis")
        self.assertNotRegex(
            config,
            r"Gateway__Edge__Forward__Secret|Gateway__StoreForwardEncryptionKey|ConnectionStrings__",
            "protected values must be supplied by provider secrets, never the manifest",
        )
        self.assertRegex(config, r'(?m)^\s*Gateway__Edge__Protocols__Gt06\s*=\s*"true"$')
        self.assertRegex(
            config,
            r'(?m)^\s*Gateway__Edge__Protocols__PacificTrack__Enabled\s*=\s*"false"$',
        )


if __name__ == "__main__":
    unittest.main()
