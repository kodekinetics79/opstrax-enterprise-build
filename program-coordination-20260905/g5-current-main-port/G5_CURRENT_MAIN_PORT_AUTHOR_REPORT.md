# G5 current-main cumulative DeviceOps port — author report

Date: 2026-09-05
Role: implementation author only; excluded from certification and final gate authority

## Exact starting point and method

- approved parent: `7debdfeac386e2da70937f78ecfa278bf196ddee`
- approved parent tree: `0929e397283d5a53180645240b1031facb3d76c4`
- branch: `integration/camera-g5-current-20260905`
- worktree: `opstrax-camera-g5-port-20260905`
- author manifest SHA-256: `ab7088e01c38176a3f1dd4f5e90723efe8654aaa516d48e3a296a2dc395413f6`

The six accepted historical G5 slices were used only as line-by-line behavior and test specifications. I reconstructed them cumulatively on the current G4 base in this order: historical check-in truth, suspension outcome, activation outcome, commissioning outcome, removal outcome, and installation/transfer outcome. I did not cherry-pick a historical commit or replace either production file wholesale. Where later old-line contexts collided, I computed a read-only three-way candidate, resolved the current-source ELD/recovery behavior in favor of the approved base, and applied only the resulting source hunks. The current attention/recovery mutations and current permission gate remain present.

The successor commit and tree cannot be embedded in their own commit. They are supplied in the post-commit handoff with the exact clean status and blobs.

## Current-base RED evidence

The first attempt to run the six new scripts could not resolve the current dependency tree. Those setup-only logs are preserved under `program-coordination-20260905/g5-current-main-port/red-results/setup-failure-red-attempt-*.tap`, all with SHA-256 `e298602e5e98f006a9f41b7b3312e9b7a75766af6ba314ae0a2792b216d34de9`; they are not product RED evidence.

After reusing the exact already-accepted dependency-remediation `frontend/node_modules` through an ignored worktree-local symlink, without an install or manifest/lock mutation, meaningful RED was recorded before production edits:

- check-in: 1 pass / 10 fail, `1967fa04e0060367ac104fbe21c83738d628dc4bcca8feec66a6dfb8e023dfd6`;
- suspension: 1 / 15 fail, `a911c84735c222d3c1f3e48afc5d6cbb0b64bba43e7cf48d83ab076daf93ca4d`;
- activation: 0 / 15, `9dfac6f73274b55c3d63857c1f789bff6ebe5840712aecf5499fc07292ccb0a7`;
- commissioning: 1 / 19 fail, `af17b2335db2ee40b49398232aa3a3b3ed01d8a6f2922ddaf33e2b3864b53596`;
- removal: 0 / 18, `12e18a18d6317f10b0f14f349b68dbfc10e0408d481314f614afcd5d12e42f91`;
- install/transfer: 0 / 30, `caa955f86b9cca7e539a0f585bd063e6dab077e63b7d5a121d8bf5a89aa032e7`.

The install/transfer script initially stopped at the absent current-base `assignmentRecordedMessage` extractor. For the meaningful RED only, I supplied a test-local extractor stub, obtained the 0/30 behavioral RED, preserved it, and restored the final script before production implementation. That extraction failure is not represented as product evidence.

## Implemented behavior

- Stored device timestamps are projected only as bounded historical check-in records, with strict explicit-offset Gregorian parsing, lifecycle precedence, no current-connectivity claim, continued polling, and current error overriding retained cache data.
- Suspension and activation return strict minimal command receipts before any display read. Rejected and unconfirmed outcomes remain distinct; no POST is retried.
- Commissioning and removal capture their immutable operator intent before preflight, preserve authorized reads, validate exact identities/version/time/result transitions, and retain known acknowledgements independently of later read failures.
- Installation and transfer capture an explicit immutable operation, accept only four disjoint strict HTTP/receipt shapes, preserve Int64 path versus safe JSON-body identity rules, issue one mutation key per manual attempt, and never claim exactly-once execution.
- Every accepted lifecycle response now requires exact HTTP success, an own strict success envelope, and a plain or null-prototype data carrier. Inherited carriers, arrays, dates, class/exotic objects, wrong-case lookalikes, conflicting aliases, unsafe numbers, and malformed present discriminators fail closed.
- All five acknowledgement refresh paths use the current active query objects, detect query replacement/removal/error/pause, release on initially offline and fail-then-pause states, remove their subscribers, qualify retained data as stale, and expose GET-only recovery. A known receipt is never downgraded because a projection refresh fails.
- Form-owned assignment, commissioning, and removal sessions retain synchronous single-flight admission, captured drafts, generation/target ownership, outer-settlement release, and stale-callback isolation. Suspension and activation preserve their accepted synchronous single-flight and receipt-generation controls.
- Customer copy remains bounded to recorded software outcomes and does not claim physical installation/removal, hardware activation, current connectivity, authenticated delivery, provider behavior, or certification.

## Exact current-source evidence

Evidence files are under `program-coordination-20260905/g5-current-main-port` in the shared coordination root.

| Evidence | Result | SHA-256 |
|---|---:|---|
| `AUTHOR_FINAL_device_checkin.tap` | 11/11 | `e43a2d787247a59934919b16bae2a836ebdac354898d911ce88b2b1753646808` |
| `AUTHOR_FINAL_device_suspension.tap` | 16/16 | `88a58f5cad3b7697ac44ba4af5cd68fd80cd0497b6fd0d96433d5ec62b5c3c22` |
| `AUTHOR_FINAL_device_activation.tap` | 15/15 | `d8f4af321a37c6f58c30032c7d438d973aa901b5eaa35687452c611378b815e7` |
| `AUTHOR_FINAL_device_commissioning.tap` | 20/20 | `6720e1407887e29c931ac03812586994d2f54810a27b972dc92d9ff93e6c6324` |
| `AUTHOR_FINAL_device_removal.tap` | 18/18 | `1692af2527d22d2b239e132b37fecbbcc5dbf553d38ce9de057cf31e92e7e298` |
| `AUTHOR_FINAL_device_install_transfer.tap` | 30/30 | `dd9506c315c40d0009b8ecb6dd596c3bb045e5e4fadf7aed0fdf42c491ce6d82` |
| `AUTHOR_FINAL_device_installation.log` | pass | `f2dfe22054b56fd398ca40691f18385e2d1d910ca93555dc9c62521d0972a0ba` |
| `AUTHOR_FINAL_g4_direct.log` | all six pass | `14fe876971c4bfa014dea536d8184bbfabbd30342bcaf0b09ca3f8028af736f5` |
| `AUTHOR_FINAL_camera29.log` | 29/29 | `66ac1c6f46819e16dfc0546f62c1cc17ef2fca631c65ee37b4faaf6324828e3c` |
| `AUTHOR_FINAL_current_query_fault_probe.tap` | 1/1 | `6eef8e7ba208ebcdf3a1cb45904286f6f1e870692a4d028174bbebb01f6427b8` |
| `AUTHOR_FINAL_current_aggregate.log` | pass | `8bf65b7594e559479f66ef8398b37df08dfb7c888cc178069b0992953eb6348d` |
| `AUTHOR_FINAL_build.log` | pass; 210 chunks; 314.86 KiB raw / 95.23 KiB gzip maximum | `8b939e9e999058a68313d108eec995280036051dce576126f4a4ce5285f7a329` |
| `AUTHOR_FINAL_budget.log` | pass | `58f3979a2996f25368b27fdb673288e2ed6ea9c09c2a73dec7212e3ad3ba8f2d` |
| `AUTHOR_FINAL_lint.log` | configured lint pass | `b1cbed49fa6389bd4cc036a66b95cff03e5177b1ea34d6cff3a38de6f769cb4b` |
| `AUTHOR_FINAL_tsc.log` | pass, no diagnostics | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |

The six named G5 suites retain exactly 110 named cases. Their existing cases now also carry plain/null-prototype, inherited/exotic, exact-casing, initially-offline, fail-then-pause, pending-release, and subscription-cleanup matrices without inflating the named-case count. The existing aggregate passes but deliberately does **not** register the six new G5 scripts because package ownership is excluded. Configured ESLint covers JS/JSX only; it is not claimed as TS/TSX/MJS lint. TypeScript compilation and directly executable MJS contracts are separate evidence.

## Source hashes before freeze

- `frontend/src/services/telematicsService.ts`: `e64873461b2e17dfdee9bbb3d31db652efd4edfb91b8ecdbf1ba1a242314af87`
- `frontend/src/pages/IotDevicesPage.tsx`: `8a488c017062f5669d0482b1445ad1049f037b6b02425f71c307ef2034774df9`
- check-in suite: `7bc14f6f2cf112aa4750681c2677538f1df56a58c689b5be92569f6ced3084f7`
- suspension suite: `c86c81bf2356e15c744bd33b599d484d55a9e50df4df4ddf08b3b8a6db2dd3be`
- activation suite: `0c6d3091fc53448a6fa71427da574300848574cf84a0f9cbe774e6664ccbaea5`
- commissioning suite: `3aace5ca4b7baf7450c0b15a0c89042d824b8818f0776cc90ee3139847b016b6`
- removal suite: `a9e8d609d6eef0dc7ea633f51b8e14076d020fc242c2ca9bc99a10e6209cde07`
- install/transfer suite: `d179516b8213a98acf772e6436715b90e5bb383662b3a22a0aedf662aba8313e`
- existing installation contract: `efc81454683407b25e355b35a35b3057dc4e0375e85e4f327c7c29cbaaf5cf3a`

`git diff --check` passed before freeze. All protected camera/G4/backend/package hashes match the approved manifest, including package `b351e99de746cfe485a53352ecdda705085deabf`, lock `9f0682b82c33a37cb869e2f1755443619d44c958`, camera contract `ae73ad5bbc5e3bab0a6fccd90c91ac760e21920c`, and the six G4 focused-suite blobs.

## Replay commands

Run from `frontend` with the accepted dependency tree available:

```text
node scripts/test-device-checkin-truth-contract.mjs
node scripts/test-device-suspension-outcome-contract.mjs
node scripts/test-device-activation-outcome-contract.mjs
node scripts/test-device-commissioning-outcome-contract.mjs
node scripts/test-device-removal-outcome-contract.mjs
node scripts/test-device-install-transfer-outcome-contract.mjs
node scripts/test-device-installation-contract.mjs
node --experimental-loader=/private/tmp/opstrax-g4-node-loader.mjs scripts/test-coaching-completion-acknowledgement.mjs
node --experimental-loader=/private/tmp/opstrax-g4-node-loader.mjs scripts/test-coaching-note-error-truth.mjs
node --experimental-loader=/private/tmp/opstrax-g4-node-loader.mjs scripts/test-coaching-editor-session.mjs
node --experimental-loader=/private/tmp/opstrax-g4-node-loader.mjs scripts/test-coaching-detail-read-truth.mjs
node --experimental-loader=/private/tmp/opstrax-g4-node-loader.mjs scripts/test-safety-detail-shape-truth.mjs
node --experimental-loader=/private/tmp/opstrax-g4-node-loader.mjs scripts/test-safety-coaching-input-truth.mjs
npm run test:camera-manual-metadata
node --test --test-name-pattern='actual exact-key QueryCache guard revokes retained detail open, submit and export before rerender' scripts/test-camera-manual-metadata-contract.mjs
npm run test:contracts
npm run build
npm run verify:bundle-budget
npm run lint
npx tsc --noEmit
```

## Governance and HOLDs

This is author evidence only. It does not certify G5, close a gate, or establish HTTP/database atomicity, backend concurrency, provider compatibility, GT06 compatibility, firmware/model behavior, physical installation/removal/suspension/activation, real commissioning, check-in authenticity, telemetry delivery, browser UX/accessibility, deployment, pilot readiness, or production readiness. No DB, browser, network, provider, device, publication, merge, or deployment action was performed. Capability truth remains `DEVELOPMENT / HOLD` pending two independent exact-candidate reviews and root/CTO disposition. Package registration remains a separately serialized future owner step.
