# OpsTrax launch browser suite

This suite separates safe anonymous checks from authenticated staging journeys.

- Production is detected from `E2E_TARGET_ENV=production` or a known production host. It rejects every auth-state variable and aborts/fails on any method other than `GET`.
- External targets without an explicit environment default to production (fail closed).
- Authenticated projects run only outside production and consume ignored Playwright storage-state files. Never commit `playwright/.auth`.
- The one mutating journey is disabled unless both HTTPS UI/API hosts are explicitly listed in `E2E_STAGING_HOSTS`, a disposable-tenant acknowledgement is exact, a canary vehicle ID is supplied, and mutation mode is explicit.
- Browser console errors, page exceptions, request failures, 5xx responses, traces, videos, and failure screenshots are captured as artifacts. Runtime exceptions or 5xx responses fail local, staging, and production checks; expected 4xx boundaries remain testable.

Bootstrap:

```bash
cd tests/e2e
npm ci
npx playwright install chromium
cp .env.e2e.example .env.e2e.local
npm run test:guard
npm run test:list
```

Use `npx playwright codegen "$E2E_UI_BASE_URL/login" --save-storage=playwright/.auth/tenant.json` to sign in manually to a dedicated staging persona. Repeat for driver, customer, and Platform personas. Storage states contain reusable credentials and must be revoked after certification.

`npm run test:staging:readonly` performs no intentional writes. `npm run test:staging:mutations` creates a labeled work order and therefore belongs only in a disposable staging tenant.
