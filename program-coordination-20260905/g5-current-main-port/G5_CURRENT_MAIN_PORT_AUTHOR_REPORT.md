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

## Successor addendum — P0 boundary and continuation corrections

The earlier frozen candidate `1e4c2df728b5f18ce98382e08eeebec0ac9f4c55`
(tree `47f62e0f967e85778b9c249ce0477aef8d5c3a35`) is preserved as rejected
evidence and remains `HOLD`. This addendum describes its bounded successor. It
supersedes the earlier source hashes and author-green evidence table above; it
does not alter the accepted historical RED, port order, scope, or HOLDs.

### Successor RED preserved before correction

The successor regressions first reproduced the defects on the frozen predecessor.
These were intended product failures, not extraction or dependency failures:

| Evidence | Intended failure | SHA-256 |
|---|---|---|
| `AUTHOR_SUCCESSOR_RED_install_transfer.tap` | inherited `success: false` incorrectly treated as a known rejection | `6e3df6890e8d7052d11b7c6e572c86097032ab19b03354051865b36ab53ca62a` |
| `AUTHOR_SUCCESSOR_RED_device_checkin.tap` | inherited/prototype lifecycle and check-in facts were consumed | `db99f45a980888f1c84f324107aa560148e54a550dfbc4c8cff729364a06436d` |
| `AUTHOR_SUCCESSOR_RED_install_transfer_boundary.tap` | lifecycle envelope spelling collision accepted | `28eaa2982a61536dc0e1d74bef3139e42ea0836f656aa58abf60b7495c8cfdbc` |
| `AUTHOR_SUCCESSOR_RED_suspension_boundary.tap` | envelope/receipt own-field boundary accepted pollution | `686bb3705e368864507a246d929081b720138dd885b01ae08510a5f4164cea99` |
| `AUTHOR_SUCCESSOR_RED_activation_boundary.tap` | envelope/receipt own-field boundary accepted pollution | `520d0922c665a0dceadc949a9f6a2b5362d2e674e0148cf4fcea1606e0fbaad6` |
| `AUTHOR_SUCCESSOR_RED_commissioning_boundary.tap` | envelope/receipt own-field boundary accepted pollution | `36e63fa8e76901d001c40bbf134bec0665834f75d6c7eaf4d94e4f0a977cae22` |
| `AUTHOR_SUCCESSOR_RED_removal_boundary.tap` | envelope/receipt own-field boundary accepted pollution | `e13feef40b378a4c4e08211c1b894424d86841ceeddaf519e08fedece4779f9a` |

### Bounded corrections

- Check-in projection now admits only plain or null-prototype carriers and consumes own exact camel/snake aliases. Conflicting aliases (including own `undefined`), inherited values, exotic carriers, arrays, and wrong-case/normalized lookalikes fail closed without inventing a current check-in.
- All five lifecycle mutation envelopes reject own wrong-case/normalized `success`/`data` lookalikes, even beside valid lowercase keys. Explicit rejection requires an allowed client status and own `success: false` on that strict carrier.
- Suspension, activation, commissioning, and removal receipts require own exact fields before normalization. Null-prototype valid receipts remain accepted; prototype-only required values do not.
- Assignment, removal, and commissioning state-writing continuations now require live permission plus the exact current target, form identity, and session generation. Suspension and activation carry an immutable target and generation through synchronous admission, success, error, and refresh. Target replacement, drawer/confirmation change, permission loss, or generation change makes older continuations inert.
- Permission loss invalidates all five mutation/refresh ownership contexts, clears pending/warning/receipt/error rendering, and cannot leave an unauthorized stale warning visible. Known receipts still use GET-only display recovery; no mutation is retried.

### Successor current-source evidence

The six named DeviceOps suites retain exactly `11 + 16 + 15 + 20 + 18 + 30 = 110` cases. New negative controls were folded into existing named cases.

| Evidence | Result | SHA-256 |
|---|---:|---|
| `AUTHOR_SUCCESSOR_FINAL_device_checkin.tap` | 11/11 | `5fba60be0924e5940fb21ca08c5562381d127dda28b51ad626ad4f077c7809fe` |
| `AUTHOR_SUCCESSOR_FINAL_device_suspension.tap` | 16/16 | `2bf5220ab4867b82b52a9b420d87398ee0ad18569bd7fced46ada062113f1aed` |
| `AUTHOR_SUCCESSOR_FINAL_device_activation.tap` | 15/15 | `bbf0251273417991821378337c8a624a81c6084a0af5cdc332fd981d69c745cb` |
| `AUTHOR_SUCCESSOR_FINAL_device_commissioning.tap` | 20/20 | `3fd4132f2c31a56cb02ed5ddddfed3cccf0e45178cd6f497470decf383c2d7a0` |
| `AUTHOR_SUCCESSOR_FINAL_device_removal.tap` | 18/18 | `f840a21e85e94ed39a7cb4eefbd0d0a4b41f955c5024eaa328fc3174073ace00` |
| `AUTHOR_SUCCESSOR_FINAL_device_install_transfer.tap` | 30/30 | `1d0651508892d15dfd81bb69bd3a25db9bbe79f6c64a24af96d59a5a1b4100cf` |
| `AUTHOR_SUCCESSOR_FINAL_device_installation.log` | 1/1 | `2cd4ce1bcbd117939fd47a7d0e557765d93cc8da52bfbcd79d1bd5dbc2b57c2c` |
| six `AUTHOR_SUCCESSOR_FINAL_g4_*.log` files | all pass | completion `5c53ea2a...`; note `88a7811e...`; editor `9d648d3d...`; detail `cb3edc29...`; shape `1b2adc6f...`; input `fd9604b9...` |
| `AUTHOR_SUCCESSOR_FINAL_camera29.log` | 29/29 | `46ffc22d23470f8b262be88a5e8714c5db1f2d2cea13177594e1fa08a1330d56` |
| `AUTHOR_SUCCESSOR_FINAL_current_query_fault_probe.tap` | 1/1 | `660aacc56e4b9dfed57057399f377ae7443fddd07b7d9e69e6f09892a1751442` |
| `AUTHOR_SUCCESSOR_FINAL_current_aggregate.log` | pass | `e1ed10f3181838a3d12a5eaf2e1067340892019bf483799d61374ce511e4c82d` |
| `AUTHOR_SUCCESSOR_FINAL_build.log` | pass; 210 chunks; maximum 314.86 KiB raw / 95.23 KiB gzip | `bfdf26cd28cbf0c0f0658cc73ec5c5a3663b74960ece93517a37f2934b5da3c7` |
| `AUTHOR_SUCCESSOR_FINAL_budget.log` | pass | `58f3979a2996f25368b27fdb673288e2ed6ea9c09c2a73dec7212e3ad3ba8f2d` |
| `AUTHOR_SUCCESSOR_FINAL_lint.log` | configured lint pass | `b1cbed49fa6389bd4cc036a66b95cff03e5177b1ea34d6cff3a38de6f769cb4b` |
| `AUTHOR_SUCCESSOR_FINAL_tsc.log` | pass, no diagnostics | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |

Successor pre-freeze source SHA-256 values:

- `frontend/src/services/telematicsService.ts`: `4213f5cab559efd1826cdb4c4c9c7f903e8d8eba170ae6dc6bf7b9a2dc4d5058`
- `frontend/src/pages/IotDevicesPage.tsx`: `1ca7f2090e3b54a697f6b002148ed7be25e5e53ab7da346341f5c3e42e198267`
- check-in: `6e42fbb88192a53bdd3315233138a716c65f5fefec21dc501fc42a99740009c1`
- suspension: `49e479740704486182dc095404b304064df4568eb0a62f3df1d6595f37d13ec9`
- activation: `63a3181cf1bfaf6ef7c99f06256a190da69938a24825a2988f7d942f472f27c0`
- commissioning: `7c908e1c044f0d7600086c128535f26630a6d78ab86ca64b88e75e8c90ee9fe6`
- removal: `dd82c618780247390fe9b706d0684bdeb3d8ba72b397ef76414093cc1ed26c1c`
- install/transfer: `897aea60047df2d61862dbe807ffd4292abec7d86eb92d31115d556559dd490e`

`git diff --check` passed. All protected package, lock, backend, camera, and G4 blobs match the accepted values recorded above. Configured lint still does not cover TS/TSX/MJS; the separate TypeScript compile and direct MJS executions are reported without inflating that claim. No browser was run because this bounded author allocation expressly prohibited browser work. No database, network, provider, physical-device, publication, merge, or deployment action was run.

This successor is implementation-author evidence only. It remains `DEVELOPMENT / HOLD` until both non-author perspectives review the exact frozen successor and the root/CTO issues a disposition.

## Governance and HOLDs

This is author evidence only. It does not certify G5, close a gate, or establish HTTP/database atomicity, backend concurrency, provider compatibility, GT06 compatibility, firmware/model behavior, physical installation/removal/suspension/activation, real commissioning, check-in authenticity, telemetry delivery, browser UX/accessibility, deployment, pilot readiness, or production readiness. No DB, browser, network, provider, device, publication, merge, or deployment action was performed. Capability truth remains `DEVELOPMENT / HOLD` pending two independent exact-candidate reviews and root/CTO disposition. Package registration remains a separately serialized future owner step.
