# OpsTrax launch browser suite

This suite separates safe anonymous checks from authenticated staging journeys.

- Production is detected from `E2E_TARGET_ENV=production` or a known production host. It rejects every auth-state variable and aborts/fails on any method other than `GET`.
- External targets without an explicit environment default to production (fail closed).
- Authenticated projects run only outside production and consume ignored Playwright storage-state files. Never commit `playwright/.auth`.
- Mutating journeys are disabled unless both HTTPS UI/API hosts are explicitly listed in `E2E_STAGING_HOSTS`, a disposable-tenant acknowledgement is exact, a canary vehicle ID is supplied, and mutation mode is explicit.
- The real IoT lifecycle adds a second exact acknowledgement, distinct source/target staging vehicles, explicit registered-device category and installation role, and an authenticated fleet administrator from another disposable tenant. It provisions and revokes a real device credential pair, installs, ingests signed telemetry, commissions from heartbeat evidence, transfers, proves delayed historical attribution without current-position overwrite, removes, revokes, and proves cross-tenant denial. Its Playwright trace/video are disabled because credentials appear once in responses and authenticated device headers; the attached evidence is deliberately redacted.
- Browser console errors, page exceptions, request failures, 5xx responses, traces, videos, and failure screenshots are captured as artifacts. Runtime exceptions, 5xx responses, API-target mismatches, and unexpected request failures fail local, staging, and production checks. The request-failure allowlist contains only an aborted GET document navigation and the exact local-anonymous `GET /api/localization/user-preferences` bootstrap when its configured local API is intentionally offline; expected HTTP 4xx boundaries remain testable.
- Authenticated staging projects fail during worker setup unless each requested persona's `E2E_*_AUTH_STATE` resolves to an existing file. A skipped-auth green run is not a certification result.
- Every authenticated staging journey must observe an application `/api/` request, and that request must use the origin and optional base path declared by `E2E_API_BASE_URL`.

Bootstrap:

```bash
cd tests/e2e
npm ci
npx playwright install chromium
cp .env.e2e.example .env.e2e.local
npm run test:guard
npm run test:list
npm run test:inventory
```

Use `npx playwright codegen "$E2E_UI_BASE_URL/login" --save-storage=playwright/.auth/tenant.json` to sign in manually to a dedicated staging persona. Repeat for driver, customer, and Platform personas. Storage states contain reusable credentials and must be revoked after certification.

`npm run test:staging:readonly` forces staging mode, performs no intentional writes, and fails closed when any tenant, driver, customer, or Platform storage state is absent. `npm run test:staging:mutations` also forces staging mode; it creates a labeled work order and therefore belongs only in a disposable staging tenant. `npm run test:staging:iot` executes the separately guarded real IoT lifecycle. `test:inventory` verifies 50 discoverable journeys (the former 49 plus this lifecycle), but prints an explicit warning that discovery is not execution. Certification must also pass `scripts/assert-execution.mjs` against the JSON result; skips never count as execution.

The `OpsTrax staging IoT certification` workflow can run manually after it exists on the default branch, or directly on a candidate PR by adding the `certify-staging-iot` label after the exact SHA is deployed. The labeled PR path reads URLs, vehicle IDs, category, and role from `Staging` environment variables and the two base64 storage states from `Staging` secrets; environment approval remains authoritative. Remove/re-add the label to request a fresh exact-head run after any push or redeploy.
