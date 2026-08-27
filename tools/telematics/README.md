# Telematics field tools

All tools use only the Python standard library.

- `fingerprint.py` is offline/read-only, caps input at 1 MiB, and never opens a network connection.
- `capture_listener.py` binds loopback by default, confines mode-0600 output to the ignored `captures/` directory, enforces an exact 1 MiB recorded-payload cap per connection, and refuses `production`. A non-loopback bind requires the staging flag plus the exact public-bind acknowledgement. Device ACKs additionally require a confirmed GT06 fingerprint and a separate active-reply acknowledgement; malformed, CRC-invalid, or cap-truncated frames are never acknowledged.
- `public_replay.py` accepts only synthetic `.hex` fixtures whose repository-relative paths pass `git ls-files --error-unmatch`, defaults to a zero-network dry-run, sends at most one frame, requires exact staging-host membership, and refuses the known production UI/API hosts. It reports hashes and sizes rather than raw reply bytes.
- `certification_harness.py` creates deterministic, non-personal native-HMAC certification scenarios for exactly 1,100 provisioned devices across the five certification branches. It defaults to a zero-network plan, never touches SQL, refuses production, requires mode-0600 one-time credential files, verifies the exact staging SHA through `/health/ready`, and redacts all API keys, HMAC secrets, signatures, nonces, and response bodies from its output.

```bash
python3 tools/telematics/fingerprint.py --self-test
python3 -m unittest discover -s tools/telematics/tests -p 'test_*.py'

python3 tools/telematics/public_replay.py \
  --fixture login.hex \
  --host gateway-staging.example.test \
  --port 5023 \
  --environment staging \
  --allow-host gateway-staging.example.test \
  --dry-run

python3 tools/telematics/certification_harness.py \
  --credentials /secure/path/CLHQ-devices-one-time-credentials.csv \
  --run-id CERT-LARGE-20260825-PREFLIGHT-01 \
  --observed-at 2026-08-27T05:15:00Z
```

Execute mode is staging-only and intentionally requires the exact candidate SHA and
the literal acknowledgement printed by `--help`. Run the plan first and preserve it
with the candidate evidence. Do not use the harness as provider-certification proof:
it validates OpsTrax's authenticated native device boundary. A provider sandbox and a
small real-device/provider pilot remain separate acceptance layers.

The bounded negative controls distinguish transport replay (same nonce) from
application idempotency: a fresh-nonce retry with the exact authenticated body is
accepted without mutation, while reuse of that `clientGeneratedId` with a changed
body is rejected.

The rebuild ran only offline unit tests and dry-run validation. It did not bind a public listener, send a frame, capture a physical device, or replay against any deployed gateway.
