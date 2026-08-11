# Launch certification rebuild evidence — 2026-08-11

This ledger separates four evidence states:

- **Prepared**: source/configuration exists and passed static or unit validation.
- **Compiled**: the relevant application/test graph parsed, type-checked, bundled, or enumerated.
- **Executed**: the named command ran in this workspace and its result is recorded below.
- **Blocked**: external authority, credentials, infrastructure, hardware, or CI execution was unavailable. A blocked item is not evidence of success or failure in that environment.

## Certification status

| Surface | Prepared | Compiled | Executed in rebuild | Blocked / not claimed |
| --- | --- | --- | --- | --- |
| Expo SDK 56 mobile | Expo-compatible versions, `expo-constants`, Babel preset, safe overrides | TypeScript, ESLint, Expo web export | Typecheck pass; lint pass; 661-module web export pass; 4 audit-policy tests pass; both 11- and 32-finding registry representations resolve exclusively to the same two reviewed advisories | Native iOS/Android build and physical-device run not executed. Online Expo compatibility check is delegated to blocking CI; local check ran offline. |
| Playwright safety/roles | Five persona projects, production GET-only interception, production auth rejection, exact staging mutation allowlist, per-worker readonly storage state, failure artifacts, and all-target runtime/5xx enforcement | 48 meaningful tests enumerated across 7 spec files | 12/12 guard tests pass | 48 browser cases were not locally executed because a Playwright Chromium binary is not installed. CI browser job is prepared but has not run in GitHub. Staging mutation case remains opt-in and secret-gated. |
| Large-data plan | 10,000 dependency-aware positive operations + 10 structured negatives; source-binder contracts; guarded runner | All requests materialize, including HMAC telemetry | 26/26 plan/executor tests pass; dry-run materialized 10,000/10,000 with zero network; mock executor completed 10,000/10,000 | No staging API or database execution. Negative pack not applied. |
| Load/stress | Read-only k6 workload; exact staging host; HTTPS; isolated-tenant acknowledgement; mode-0600 credential file; hard caps | k6 script statically inspected as two GETs only | 10/10 guard/static tests pass | k6 binary and isolated staging credentials unavailable; no load or stress traffic sent. |
| Telematics tools | Offline fingerprint, loopback-first capture with an exact 1 MiB per-connection payload cap, CRC-validated ACK, confined mode-0600 captures, git-tracked one-shot public staging replay | Python imports/CLI paths validated | 18/18 unit tests pass; 9/9 fingerprint vectors pass; public replay dry-run reports zero network | No public listener bind, physical-device capture, provider traffic, or deployed-gateway replay. PT40 protocol remains unconfirmed without a physical capture. |
| Stage76 terminal ordering | Predeploy, clean-chain, production rehearsal and CI all place Stage76 after Stage58/59/67; exact-SHA ledger includes new jobs | Shell syntax and 6/6 CI-contract tests | Static/order tests executed | Disposable Postgres clean-chain/rehearsal and GitHub Actions were not executed in this workspace. Production migration was not applied. |
| Publish/deploy | Branch publication and a draft PR are authorized; the exact-SHA mandatory gate graph is prepared | Workflow source validated locally | Local release-candidate validation recorded here; GitHub records the subsequent commit, branch, PR, and CI identities | Merge, package/image publication, production deployment, and provider actions are not authorized and remain gated on exact-SHA CI evidence. |

## Executed command evidence

```text
mobile: npm run typecheck                                      PASS
mobile: npm run lint                                           PASS
mobile: EXPO_NO_TELEMETRY=1 EXPO_OFFLINE=1 npm run build       PASS (661 modules)
mobile: npm run test:audit-policy                              PASS 4/4
mobile: npm audit + validate-audit.mjs                         POLICY PASS (this runner: 11 high, 0 critical)
mobile: retained dependent-expanded audit + validator          POLICY PASS (32 high, 0 critical)

tests/e2e: npm run test:guard                                  PASS 12/12
tests/e2e: npm run test:list                                   COMPILED 48 tests / 7 files

tools/launch: node --test test_launch_plan.mjs                 PASS 26/26
tools/launch: node --test test_ci_contract.mjs                 PASS 6/6
tools/launch: execute_launch_plan.mjs --dry-run                PASS 10,000 materialized; 0 network

tests/load: node --test test_load_guard.mjs                    PASS 10/10

tools/telematics: python3 -m unittest discover ...             PASS 18/18
tools/telematics: fingerprint.py --self-test                   PASS 9/9 vectors
tools/telematics: public_replay.py ... --dry-run               PASS 1 fixture; 0 network
```

## Mobile dependency advisory disposition

The compatibility baseline follows the official [Expo SDK 56 documentation](https://docs.expo.dev/versions/v56.0.0/): React Native 0.85, React 19.2.3, and Node 20.19 or newer. The project uses Node 22.23.2 in CI and did not downgrade the framework.

Safe direct/transitive overrides removed the fixable `brace-expansion`, `js-yaml`, and `uuid` findings without changing Expo 56. The only remaining terminal advisory sources are `image-size` GHSA-w3rx-r6r6-pgpr and GHSA-5p2g-fcmc-qvqq, propagated through Expo/Metro/React Native dependents. npm's suggested remediation downgrades to Expo 53 / React Native 0.72, which violates SDK 56 compatibility and is not accepted.

npm 11.9 returned two valid representations during final verification: this runner enumerated 11 affected packages, while a retained fresh response enumerated 32 by expanding React Navigation, Expo modules, and React Native peer dependents. Both contain the same two terminal advisory IDs and zero critical findings. CI therefore does not hard-cap dependent count or blindly allowlist expanded package names: it recursively derives the permitted closure from `via` edges terminating at advisory sources 1138808/1138809. It fails unknown advisory objects, unresolved edges, rootless cycles, non-high or critical findings, and inconsistent metadata; resolved findings may disappear automatically after an upstream fix.

## Load caps and mutation boundary

| Profile | Maximum HTTP requests/s | Duration | Max VUs | Methods |
| --- | ---: | ---: | ---: | --- |
| smoke | 2 | 30s | 4 | GET only |
| load | 10 | 300s | 20 | GET only |
| stress | 20 | 600s | 50 | GET only |

Playwright production projects reject any configured role state and abort every method other than `GET`, including `HEAD` and `OPTIONS`. The sole browser mutation case needs `E2E_TARGET_ENV=staging`, both UI/API hosts in `E2E_STAGING_HOSTS`, the exact disposable-tenant acknowledgement, tenant auth state, and a numeric canary vehicle ID. It is not part of default production or local browser execution.

## Remaining launch blockers

1. Run the blocking GitHub workflow at the exact candidate SHA, including mobile and local Chromium journeys.
2. Run clean-chain and production-shaped Stage76 rehearsals on disposable Postgres; retain their artifacts.
3. Run readonly load/stress only against an explicitly allowlisted isolated staging tenant and retain k6 thresholds/results.
4. Execute authenticated staging persona journeys with independently provisioned readonly role states; separately authorize the single mutation canary if desired.
5. Capture and fingerprint one physical PT40 frame before recording any device protocol claim.
6. Merge or deploy only after the repository's exact-SHA evidence gate succeeds and an authorized operator supplies provider credentials/approval.
