# Telematics field tools

All tools use only the Python standard library.

- `fingerprint.py` is offline/read-only, caps input at 1 MiB, and never opens a network connection.
- `capture_listener.py` binds loopback by default, confines mode-0600 output to the ignored `captures/` directory, enforces an exact 1 MiB recorded-payload cap per connection, and refuses `production`. A non-loopback bind requires the staging flag plus the exact public-bind acknowledgement. Device ACKs additionally require a confirmed GT06 fingerprint and a separate active-reply acknowledgement; malformed, CRC-invalid, or cap-truncated frames are never acknowledged.
- `public_replay.py` accepts only synthetic `.hex` fixtures whose repository-relative paths pass `git ls-files --error-unmatch`, defaults to a zero-network dry-run, sends at most one frame, requires exact staging-host membership, and refuses the known production UI/API hosts. It reports hashes and sizes rather than raw reply bytes.

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
```

The rebuild ran only offline unit tests and dry-run validation. It did not bind a public listener, send a frame, capture a physical device, or replay against any deployed gateway.
