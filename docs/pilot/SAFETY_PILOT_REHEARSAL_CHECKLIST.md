# Safety pilot two-pass rehearsal checklist

Run this checklist twice from a clean fixture reset, using different operators. Rehearsal 1 is the technical dress rehearsal; fix every P0/P1 and reset. Rehearsal 2 is the release-candidate acceptance run. Evidence from rehearsal 1 cannot approve a changed candidate. The two runs must use the same immutable Git SHA, frontend/API/gateway image digests, migration-manifest hash, configuration revision and Platform commercial snapshot; otherwise restart both runs.

## Evidence record required for every scenario

Give every run a unique `R1-*` or `R2-*` identifier and record one row per scenario ID below. A row is incomplete unless it contains: run ID, scenario ID, persona, UTC start/end, browser viewport, expected result, actual result, observable HTTP/result code when applicable, before/after row version or state, synthetic business ID, audit/correlation ID, screenshot or video path, console/network finding, severity, defect/exception ID and operator initials. Evidence paths must be relative to the immutable release bundle and contain no secret, cookie, token, password, connection string or client data.

`PASS` means the rendered state, persisted state and audit evidence agree. `BLOCKED`, `NOT RUN`, `PARTIAL`, a missing artifact, or a result inferred only from source/tests is not a rehearsal pass. API probes may supplement rendered proof for concurrency and isolation, but cannot replace the browser step named by the scenario.

## Before each rehearsal

- [ ] Record run ID, UTC start, operator, candidate SHA and immutable image digests.
- [ ] Confirm the worktree/build context is clean and migration hashes match the evidence manifest.
- [ ] Confirm environment is `Production`, HTTPS is effective, demo seed and simulator are disabled, and `/health/ready` plus `/health/deep` return 200 with the expected version.
- [ ] Confirm runtime DB users are exactly the restricted application and system roles, distinct from each other and the owner.
- [ ] Apply migrations with the owner identity; capture the fourteen required ledgers for Stages 47, 58, 59 and 65–75, including Stage 47's detention evidence schema, Stage 73's null-safe fail-closed HOS offboarding reconciliation, Stage 74's Production retention-policy contract and Stage 75's default-off bounded support-access contract.
- [ ] For Stage 47, separately prove the five detention tables, FORCE RLS, signed-ticket policies, restricted role/sequence grants and the null-safe dual-gated immutability trigger. Record charge/outbox integration columns and indexes as present or out of scope; the ledger alone cannot prove them because those integrations are predecessor-dependent.
- [ ] Invoke the audited canonical reset and record its reset audit IDs, deleted-row count and newly created opaque tenant ID. A normal idempotent seeder call is not a clean reset.
- [ ] Verify the exact fixture-v7 baseline below before opening the first persona session. Stop on any mismatch; do not repair rows manually.
- [ ] Export the Platform control snapshot. Verify tenant Active, approved market packs, explicit module states and no unsupported integration.
- [ ] Confirm external monitor is green, the named support owner is reachable, rollback target is available and evidence recording is started.

## Authoritative fixture-v7 baseline

IDs are resolved from stable business keys after each reset; database surrogate IDs are intentionally allowed to change.

| Contract | Exact acceptance before each run |
|---|---|
| Tenant/commercial | one active `MERIDIAN-DEMO` tenant; `entitlement_policy_mode=package_allowlist`; exactly the nine governed keys (`safety`, `maintenance`, `dispatch`, `telematics`, `crm`, `customer_portal`, `reports`, `compliance`, `integrations`) explicitly enabled by the fixture |
| Organization/personas | two active branches (`MER-NORTH`, `MER-SOUTH`); seven tenant identities total; one active Fleet Manager, Customer Portal User, Safety Manager, Driver, Dispatcher, Maintenance Manager and Safety Auditor; Driver identity linked only to `MER-DRV-1` |
| Core counts | five vehicles, five drivers and twelve jobs; compare the complete reset manifest, not these three headline counts alone |
| Safety signals | `MER-SAFE-1` and `MER-SAFE-2`, branch-owned and linked to deterministic drivers/vehicles |
| Incident | exactly one `MER-INC-1`, `Under Review`, linked to `MER-SAFE-2`; its seeded child is labelled **Synthetic harsh-braking telemetry metadata**, has no URL, and states `synthetic=true`, `verificationStatus=not_verified`, `custodyStatus=not_managed`, `retrievalStatus=not_available` |
| Coaching/score | `MER-COACH-1=Assigned`, `MER-COACH-2=Completed`; two safety-score rows with formula version, observation windows and non-null computation time |
| DVIR | `MER-DVIR-1` has one major open out-of-service brake defect linked to `MER-WO-DVIR-1`; `MER-DVIR-2` is submitted with no defect and safe-to-operate |
| HOS | one current warning clock for `MER-DRV-1` with 165 drive minutes remaining; one closed, uncertified 150-minute demo log for the current fixture date |
| ELD | one branch-owned `MER-ELD-*` device in `Diagnostic`, provider sync `Healthy`, no API-key hash and no HMAC secret |
| Drift | no unexplained extra canonical tenant, duplicate stable key, orphaned Safety child, prior rehearsal mutation, or stale fixture version |

Platform Admin is a separate control-plane identity and is never a tenant fixture persona. The Safety Auditor is the limited/read-only tenant persona; Driver, Dispatcher and Maintenance provide additional negative-role proofs.

## Personas

Do not share one all-powerful account for the entire demo.

| Persona | Positive proof | Negative proof |
|---|---|---|
| Platform Admin | tenant/package/entitlement visibility and audited approved change | cannot silently impersonate a tenant persona or bypass required audit |
| Fleet Manager | cross-fleet Safety overview within tenant | cannot use Platform control-plane APIs |
| Safety Manager | incident, coaching and scorecard workflows in assigned scope | cannot access another tenant/branch |
| Dispatcher | contextual view needed for active operations | cannot manage coaching, certification or Platform controls |
| Maintenance | DVIR defect review and repair certification | cannot certify Driver HOS or manage Platform controls |
| Driver | own DVIR, HOS and coaching acknowledgement | cannot select/guess another driver or manager-only transition |
| Denied/limited user | permitted low-risk navigation | disabled entitlement and direct URL/API fail closed with clear UX |

## Critical scenario sequence

Record actual result, HTTP/result code where observable, screenshot/video reference, created business IDs and defect ID. Use synthetic identifiers only.

1. **Authentication and controls**
   - [ ] `AUTH-01` Each named persona logs in separately, lands on the intended route, displays the correct tenant/persona and logs out; browser back/forward cannot revive an ended session.
   - [ ] `AUTH-02` A legitimate mutation carries the issued CSRF contract; missing and mismatched CSRF are rejected with no row/audit mutation.
   - [ ] `CTRL-01` Export the complete Platform snapshot and prove fixture v7 uses `package_allowlist` with all nine explicit enabled rows.
   - [ ] `CTRL-02` Platform Admin disables `safety` with a reason; tenant navigation hides Safety after auth refresh, direct `/incidents` shows the plan boundary, and `/api/incidents` returns 403 without leaking rows.
   - [ ] `CTRL-03` Re-enable `safety`; refreshed navigation, deep link and API recover. Export after-snapshot and account for only the planned entitlement/audit diff.
   - [ ] `CTRL-04` Disable `telematics` while `compliance` remains enabled; HOS remains usable and the ELD portion shows an explicit unavailable/degraded state. It must not show empty/healthy or issue unauthorized ELD calls. Restore before continuing.
2. **Incident and external-reference metadata**
   - [ ] `INC-01` Seeded `MER-INC-1` renders `Under Review`, the connected event/driver/vehicle and the synthetic metadata disclaimer. It must not label the seed verified, retrievable, uploaded or held in custody.
   - [ ] `INC-02` Blank create and create without any driver/vehicle/Safety/dashcam link remain disabled or return 400, with zero incident and audit rows.
   - [ ] `INC-03` Create a new incident with a timezone-qualified occurrence, factual location/summary, authorized branch asset and a unique idempotency key. Expect 201, `New`, row version 1 and one create audit.
   - [ ] `INC-04` Replay the identical create: expect 200 and the same ID/version with no duplicate/audit. Reuse the key with a changed fact: expect 409 and no mutation.
   - [ ] `INC-05` Attempt `Under Review → Evidence Collected` before attaching a qualifying HTTPS reference: expect 409. Attach an approved synthetic rehearsal reference and caller-supplied SHA-256: expect one child, parent version increment and one audit. Repeat with the stale version: expect 409 and no second child.
   - [ ] `INC-06` Confirm the UI states that the attachment is only an external reference and that OpsTrax does not upload or verify the file. Do **not** claim authorized object retrieval, hash verification, malware scanning, legal hold or chain-of-custody; those are outside the current implementation.
   - [ ] `INC-07` Move only through supported forward states and create the insurance **draft**. Confirm no generated external file/ready claim. Close the incident, then prove edits, evidence and insurance children are rejected as immutable.
   - [ ] `INC-08` Safety Auditor/Dispatcher cannot mutate; a South-branch or foreign-tenant identifier yields the designed non-disclosing denial and never returns North-branch facts.
3. **Coaching and score provenance**
   - [ ] `COACH-01` Create a coaching task from `MER-SAFE-1` with driver, type, title, description and unique idempotency key. Blank required fields remain disabled/400.
   - [ ] `COACH-02` Identical create replay returns the same task without duplication; conflicting key reuse and a foreign/cross-branch driver/source are rejected.
   - [ ] `COACH-03` Assign the task and record a manager note through allowed transitions; stale row version and invalid transition return 409 without partial mutation.
   - [ ] `COACH-04` Driver sees and acknowledges only their own assigned task with the exact acknowledgement note. Driver cannot select another driver or execute manager completion.
   - [ ] `COACH-05` Manager completion is disabled until a nonblank outcome and observed score 0–100 are entered. Negative and >100 scores are rejected. Success persists `Completed`, the observed after-score, a Completion Outcome note and audit; UI labels the score observational rather than computed improvement.
   - [ ] `SCORE-01` Show score source, formula/version, 7/30/90-day observation windows and evaluation timestamp. Missing/unknown inputs render unavailable, never healthy zero.
4. **DVIR defect-to-repair**
   - [ ] `DVIR-01` Driver creates and submits a pre-trip DVIR for an authorized vehicle with one Critical/Major defect and accepts the exact attestation. Expect branch/driver derived from session and out-of-service state.
   - [ ] `DVIR-02` Identical duplicate submit is idempotent; changed-payload key reuse and cross-branch vehicle are rejected. A stale row version loses with 409 and no partial child/audit.
   - [ ] `DVIR-03` Verify the out-of-service defect affects every dispatch/availability surface promised to the client. If no executable dispatch enforcement exists, record P1 and remove that promise; a DVIR badge alone is not proof.
   - [ ] `DVIR-04` Maintenance cannot certify while any OOS defect is unresolved or without persisted repair evidence. Review, resolve the defect, and certify repair with the maintenance attestation and audit timeline.
   - [ ] `DVIR-05` Only the owning Driver can acknowledge certified repairs; wrong Driver and back-office impersonation are denied. After acknowledgement, the detail/timeline remains consistent and immutable submitted facts cannot be edited/archived.
5. **HOS and ELD**
   - [ ] `HOS-01` Driver sees only `MER-DRV-1` clock/logs. Back-office personas may review within scope but cannot certify for the Driver.
   - [ ] `HOS-02` Certification control is disabled until the exact attestation is accepted. Certify the closed seeded day; expect one immutable snapshot/certification and idempotent duplicate replay.
   - [ ] `HOS-03` Supplemental API/database adversarial evidence proves open, overlapping, cross-date or duration-mismatched segments cannot be certified and a material underlying change invalidates or is blocked. This is not currently a browser-edit scenario and must not be represented as one.
   - [ ] `HOS-04` Supplemental restricted-role evidence proves certified-log/certification deletion is blocked outside the dual-gated system offboarding path.
   - [ ] `ELD-01` Authorized manager sees the credential-free seeded `Diagnostic` state and the non-certified-ELD disclaimer; Driver/Dispatcher/foreign branch cannot mutate or enumerate it.
   - [ ] `ELD-02` Record a malfunction with code and description. Verify row-version increment, malfunction history and audit; stale/concurrent update returns 409.
   - [ ] `ELD-03` Resolution is disabled without operational evidence. Record recovery evidence; without provisioned credentials the device must remain `Diagnostic`, never falsely become `Active` merely because the fixture says provider sync is healthy.
   - [ ] `ELD-04` Provider stale/unavailable/unknown state renders as degraded/unavailable with recovery guidance and no regulatory certification claim.
6. **Negative, resilience and UX**
   - [ ] `NEG-01` Unauthenticated calls return 401; forbidden role/entitlement calls return 403; scoped foreign IDs return the designed non-disclosing 404/400; stale/idempotency conflicts return 409; missing `If-Match` where required returns 428. Do not expect undocumented 412 responses.
   - [ ] `NEG-02` Empty, API-error, slow-response and storage/provider-down experiences are distinguishable, keyboard reachable, focus-contained in dialogs and recoverable without duplicate mutation.
   - [ ] `OPS-01` Trigger one controlled critical-worker failure in the rehearsal environment. Readiness/deep health becomes non-ready, external monitoring delivers an alert to the named owner within the threshold, logs correlate it, and recovery produces a second external notification/timeline.
   - [ ] `UX-01` At the supported Chrome viewport, complete keyboard/focus checks, critical accessibility scan, reload/deep-link/duplicate-click/concurrent-click checks, console review and measured critical-path latency. Any uncaught exception, secret-bearing network artifact or unexplained request is a defect.
7. **Closeout**
   - [ ] `CLOSE-01` Export the post-run Platform snapshot; explain every diff and restore the approved pre-client posture.
   - [ ] `CLOSE-02` Run the fixture/integrity verifier against post-run state. Confirm no unexplained duplicate, orphan, cross-scope row, background exception or evidence drift.
   - [ ] `CLOSE-03` Capture exact-candidate ready/deep health, monitor/dashboard, correlated logs, created business IDs and audit IDs. Record UTC end, elapsed story and total time, defects, fallbacks and operator assessment.
   - [ ] `CLOSE-04` Reset again and prove fixture v7 returns to the exact baseline. The reset after Rehearsal 1 starts Rehearsal 2; the reset after Rehearsal 2 proves the client-demo recovery position.

## Current implementation boundaries that affect acceptance

- Incident evidence is an external-reference metadata registry. It stores a caller-supplied HTTPS URL and SHA-256 string but does not upload, fetch, hash, authorize access to, malware-scan, retain or place the object on legal hold. The fixture’s metadata sample is deliberately URL-free and explicitly unverified/unmanaged. Any promise of durable evidence custody or authorized retrieval is a P1 until separately implemented and exercised.
- Insurance output is a persisted draft; it is not a generated insurance file or transmission.
- OpsTrax provides HOS monitoring and integration workflow but is not itself a certified ELD. The connected provider/device and carrier process remain authoritative.
- HOS mutation/invalidation/deletion adversarial cases and true concurrent requests require supplemental API/database evidence because the current UI does not expose unsafe record editing. Label that evidence accurately.
- DVIR out-of-service impact is not accepted from the DVIR detail alone. The exact promised dispatch/vehicle-availability consumer must deny or clearly hold the asset.

## Severity and rehearsal outcome

- P0: security/privacy breach, data loss/corruption, wrong tenant/persona, unavailable critical demo path. Stop immediately; candidate rejected.
- P1: materially broken promised workflow, misleading compliance/safety result, no recovery/alert path. Candidate rejected.
- P2: visible defect with a safe, documented workaround that does not change the customer promise. Requires Product and QA disposition.
- P3: cosmetic/documentation issue. Track before general availability.

Both rehearsals must finish with zero open P0/P1. A workaround cannot downgrade a P0/P1. Any candidate change after Rehearsal 2 invalidates affected evidence and requires risk-based rerun, with security/auth/schema/control-plane changes requiring a full rerun.
