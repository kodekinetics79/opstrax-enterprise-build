# OpsTrax launch browser suite

This suite separates safe anonymous checks from authenticated staging journeys.

- Production is detected from `E2E_TARGET_ENV=production` or a known production host. It rejects every auth-state variable and aborts/fails on any method other than `GET`.
- External targets without an explicit environment default to production (fail closed).
- Authenticated projects run only outside production and consume ignored Playwright storage-state files. Never commit `playwright/.auth`.
- The one mutating journey is disabled unless both HTTPS UI/API hosts are explicitly listed in `E2E_STAGING_HOSTS`, a disposable-tenant acknowledgement is exact, a canary vehicle ID is supplied, and mutation mode is explicit.
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
```

Use `npx playwright codegen "$E2E_UI_BASE_URL/login" --save-storage=playwright/.auth/tenant.json` to sign in manually to a dedicated staging persona. Repeat for driver, customer, and Platform personas. Storage states contain reusable credentials and must be revoked after certification.

`npm run test:staging:readonly` forces staging mode, performs no intentional writes, and fails closed when any tenant, driver, customer, or Platform storage state is absent. `npm run test:staging:mutations` also forces staging mode; it creates a labeled work order and therefore belongs only in a disposable staging tenant.
