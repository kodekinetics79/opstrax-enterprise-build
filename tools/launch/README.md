# Deterministic launch-data plan

`generate_launch_plan.mjs` creates a deterministic dependency-aware plan of 10,000 synthetic operations plus ten negative cases. Every positive body is checked against the current API route and binder contract. Vehicle VINs use the permitted 17-character alphabet and a calculated check digit; device IMEIs are 15 digits with a valid Luhn check digit. Generation and both dry-run paths make no network calls.

```bash
node --test tools/launch/test_launch_plan.mjs
node tools/launch/generate_launch_plan.mjs --dry-run
node tools/launch/generate_launch_plan.mjs --out tools/launch/generated/plan.json
node tools/launch/execute_launch_plan.mjs --dry-run
```

The output directory is ignored and created mode `0700`; plan files are written mode `0600`. The executable dry-run resolves all resource dependencies, the required existing `customerId` fixture, and telemetry HMAC request construction without calling `fetch`.

Actual execution is deliberately hard-gated. Copy `.env.local.example` to the ignored `.env.local` using `install -m 600`, fill it only with disposable staging values, explicitly list the API hostname in `LAUNCH_STAGING_HOSTS`, and set the exact acknowledgement. The executor refuses HTTP, known production hosts (including the Vercel UI and Render API), plans below 10,000 operations, and caps above 20,000. It stops on the first non-2xx response and never prints credentials.

```bash
install -m 600 tools/launch/.env.local.example tools/launch/.env.local
node tools/launch/execute_launch_plan.mjs --execute --plan tools/launch/generated/plan.json
```

Positive execution was compiled and mock-executed by unit tests. It has not been run against a staging database in this rebuild. Negative cases are structured certification cases and require the optional cross-tenant fixture IDs; they are not automatically applied by the positive executor.
