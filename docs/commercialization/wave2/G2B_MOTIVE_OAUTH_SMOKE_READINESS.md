# G2B Motive OAuth smoke-test readiness

Date: 2026-09-02. Owning issue: #116. Implementation PR: #118.

## Boundary and authorization

The program owner asked to configure the open Motive developer app and start testing. This change prepares a narrow candidate-evaluation harness inside the existing G2B lane. It does not select Motive as the certified ELD partner, replace G2A Samsara, activate the Wave 4 production Motive connector, or close any gate. G2A remains PILOT / HOLD; the production Motive connector and certified ELD/HOS claims remain ROADMAP. Local harness implementation is development evidence only.

The frozen G1A frontend/API candidate remains `e2230425a8e14249d2c0f477a7ec7b713a6ab27e`. Do not deploy this PR into that lane. No master-plan status change or capability promotion is made by this document.

Read-only Chrome inspection of the `opstrax` Render workspace confirmed `opstrax-staging-api` (`srv-d9u6qnajobas73ef2590`) still deployed at that exact G1A SHA. The workspace lists this API and one legacy static site, not an isolated Wave 2 API. No hosting settings were changed; isolated provisioning is still an entry condition.

## Observations and corrections

- Chrome displays Motive Developer Portal app `87892`, not a Samsara developer application. The OAuth success redirect is blank. A later inspection showed 57 of 59 permissions selected; these have not been saved or accepted as least privilege by this work.
- The separate webhook draft points at a frontend root URL. No Motive webhook receiver has been implemented or tested. Leave webhooks inactive; the OAuth callback is not a webhook destination.
- Provider app credentials are visible in the portal but have not been installed in OpsTrax. Do not copy them into source, issue comments, screenshots or test fixtures. Replace the previously shared webhook secret before any future webhook use.
- The new OAuth path requests exactly nine read-only scopes, keeps app credentials server-side, and stores an access token encrypted only after all nine endpoint probes pass.
- Protected ten-minute state binds tenant, integration, actor, operation generation and a browser nonce. A Secure/HttpOnly/host-only cookie plus an authenticated non-consuming preflight must pass before the UI navigates to Motive. A missing or blocked cookie fails closed; do not disable browser security or introduce a cookie-less fallback.
- A short database transaction claims state once before provider HTTP. Network calls do not hold the privileged transaction. Finalization rechecks the generation and the initiating user's current tenant/active status/permissions/entitlement. Duplicate callbacks, revocation, disconnect and failed probes cannot restore credentials.
- Existing tokens are cleared on reauthorization. Failed exchange/probes retain no provider tokens. The rotating refresh token is deliberately not persisted until an atomic refresh workflow exists. Expiry requires reauthorization.
- Generic create/update/configure routes cannot inject Motive credentials. Callback paths use sensitive/login rate limits, no-store responses and sanitized audit records. Provider redirects are disabled. Total endpoint-probe budget is 25 seconds, below the 30-second browser request timeout.

## Exact read-only permissions

| Scope | Bounded probe |
|---|---|
| `companies.read` | `/v1/companies` |
| `users.read` | `/v1/users?per_page=1` |
| `vehicles.read` | `/v1/vehicles?per_page=1` |
| `eld_devices.read` | `/v1/eld_devices?per_page=1` |
| `locations.vehicle_locations_list` | `/v1/vehicle_locations?per_page=1` |
| `hos_logs.hours_of_service` | `/v1/hours_of_service?per_page=1&start_date=<UTC today>&end_date=<UTC today>` |
| `hos_logs.hos_violation` | `/v1/hos_violations?per_page=1` |
| `hos_logs.logs` | `/v1/logs?per_page=1` |
| `inspection_reports.read` | `/v1/inspection_reports?per_page=1` |

The portal renders two entries named “HOS Logs”; verify the exact underlying scope before saving. A label alone is not evidence of the correct permission. No write, dispatch, messages, card, camera, or webhook-management scope is requested.

## Destination contract

- Authorization: `https://gomotive.com/oauth/authorize`.
- API: `https://api.gomotive.com`.
- Callback: the isolated backend's configured `PUBLIC_API_URL` followed by `/api/integrations/motive/oauth/callback`. It must exactly match the portal's Success Redirect URI. No production/stable-staging hostname is approved by this document.
- Frontend result: isolated `PUBLIC_APP_URL` followed by `/integrations`.
- Token: default `https://keeptruckin.com/oauth/token`; configurable only between that exact URL and `https://gomotive.com/oauth/token`. Official documentation conflicts. Credential-free probes on 2026-09-02 returned OAuth JSON/400 from KeepTruckin and HTML/404 from gomotive. This supports the test default, not a successful exchange claim.
- Backend secret keys: `MOTIVE_CLIENT_ID`, `MOTIVE_CLIENT_SECRET`; optional `MOTIVE_REDIRECT_URI` and `MOTIVE_TOKEN_ENDPOINT`. These are server-only settings, never `VITE_*` values.

Official sources reviewed: [OAuth token guide](https://developer.gomotive.com/docs/generate-an-oauth-token), [conflicting overview](https://developer.gomotive.com/reference/overview-1), [scope catalog](https://developer.gomotive.com/docs/oauth-scopes), [HOS contract](https://developer.gomotive.com/reference/fetch-a-list-of-company-drivers-with-hours-of-service-hos).

## Evidence and remaining entry conditions

Independent provider, AppSec and SDET AI reviews returned LIMITED GO for code readiness for an isolated OAuth/read-only smoke-test boundary only. They are not human regulatory sign-off or live-test acceptance. The final reviewed working tree passed API/test compilation with zero errors, 20/20 PostgreSQL callback/preflight cases, 104/104 focused Motive/secret/route cases, 2,156/2,156 non-database regressions, and frontend lint/contracts/production-build/bundle-budget checks. These suites overlap; do not add their counts. Existing compiler/analyzer warnings and a vulnerability-feed availability warning remain. The exact implementation SHA must be recorded in PR #118 / Issue #116 after this snapshot is committed; hosted CI and live evidence are still pending.

The self-contained PostgreSQL tests create and remove only a random schema in an explicitly supplied local test database. They exercise actual callback transactions, encryption and audit behavior using provider HTTP doubles. They do not prove restricted-role RLS or real Motive behavior.

Before live testing:

1. Freeze an exact implementation SHA; pass hosted CI and deploy isolated frontend/API/database identities without changing G1A.
2. Identify an authorized Motive test fleet/company. A developer-portal app alone does not establish access to fleet data.
3. Obtain action-time approval for persistent portal settings and for copying app credentials into the named isolated backend's secret store. Save only the exact callback and verified nine scopes; do not activate webhooks.
4. Capture the visible same-browser preflight, consent, callback, endpoint result, denial, expiry, replay and disconnect journeys without exposing secrets. Empty authorized data proves endpoint access only, not ingestion or customer workflow readiness.
5. Disconnect locally and uninstall/revoke the provider grant at the end of the test; verify provider revocation separately.

No live OAuth exchange, provider data ingestion, sync, refresh, backfill, reconciliation, operational monitoring, representative performance, hardware, marketplace acceptance or ELD certification is claimed. Response-size bounds and restricted-role/live-browser assurance remain necessary before broader rollout.
