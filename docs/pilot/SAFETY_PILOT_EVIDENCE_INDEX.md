# Safety pilot evidence index

This is the evidence contract for a release, not a list of hoped-for checks. Copy this table into the candidate evidence bundle and replace `PENDING` only with a relative artifact path. Every artifact must identify the candidate commit, environment, UTC time, operator and scenario/gate ID.

## Bundle identity

| Field | Required value |
|---|---|
| Release candidate | immutable Git SHA; annotated release tag after GO |
| Images | registry, repository and `sha256:` digest for frontend, API and gateway if used |
| Database | environment name and opaque database/branch identifier; never a connection string |
| Migration contract | ordered migration filenames and SHA-256 hashes |
| Pilot tenant | non-secret tenant code/opaque ID and fixture version |
| Rehearsals | two run IDs, operators and UTC start/end |
| Decision | signed decision-record path and effective/expiry time |

## Required artifacts

| Gate | Artifact | Acceptance | Index path |
|---|---|---|---|
| RC-01 | `candidate-provenance/candidate.tsv`, bundle hash, image/SBOM table and published registry digests | clean tree; source/migrations/dependencies and all three SBOMs hashed; deployable references use approved registry `@sha256` digests | PENDING |
| RC-02 | `opstrax-release-candidate-<SHA>` artifact digest, CI context, mandatory-gate table and run URL | downloaded manifest SHA equals candidate; all mandatory jobs passed; no skipped/allowed-failure gate | PENDING |
| SEC-01 | redacted `health-live.json`, `health-ready.json`, `health-deep.json` | HTTP 200; Production; expected version; zero fail checks | PENDING |
| SEC-02 | `database-boundary.txt` and adversarial test report | exact restricted roles, RLS forced, no public/legacy policy, tenant/branch/persona denials pass | PENDING |
| SEC-03 | deployed-edge report | TLS/CORS/CSP/headers, same-origin proxy, session/CSRF and abuse-control cases pass for the exact public origin | PENDING |
| SEC-04 | credential-rotation receipt | provider audit/reference, UTC/operator, target secret update/redeploy, old-credential rejection and healthy distinct restricted app/system roles; no credentials in artifact | PENDING — open exposure record is stop-ship |
| CTRL-01 | `platform-controls-before.json` and `platform-controls-after.json` | complete snapshot; no unexplained diff | PENDING |
| DATA-01 | fixture-v7 manifest and three reset outputs (pre-R1, pre-R2, post-R2) | `package_allowlist`; all nine governed keys explicit; all personas/scenarios present; each reset gives the same business keys/counts and no verified/custody claim for synthetic incident metadata | PENDING |
| UAT-01 | browser run report with screenshot/video references | every critical workflow passes for its authorized persona | PENDING |
| UAT-02 | negative/degraded run report | expected 401/403/404/409/428 outcomes and honest UI states; no data leak | PENDING |
| OPS-01 | monitor dashboard export, alert receipt and incident timeline | synthetic failure reaches named owner within threshold; recovery recorded | PENDING |
| DR-01 | PITR drill log and restored-app validation | agreed RPO/RTO met; restored Safety records/evidence references consistent | PENDING |
| REL-01 | deploy/config rollback rehearsal | known-good digests/schema compatibility/config recovery and write-freeze/resume evidence meet target | PENDING |
| DATA-02 | retention/privacy configuration and export/delete exercise | values match agreement; worker/object store effective; exercise succeeds | PENDING |
| DEMO-01 | rehearsal-1 and rehearsal-2 scenario ledgers keyed to `SAFETY_PILOT_REHEARSAL_CHECKLIST.md` | every named scenario has rendered/persisted/audit evidence as applicable; immutable candidate identity agrees; different run IDs/operators; resettable, complete, within demo timebox; no P0/P1 | PENDING |
| UX-01 | browser/accessibility/performance report | supported viewport, keyboard/focus, critical scan, console and latency/error-state checks pass | PENDING |
| GOV-01 | independent review dispositions | named reviewers, scope, findings, resolution/exception links | PENDING |
| GOV-02 | completed executive decision record | all required signatures and support/rollback ownership | PENDING |
| GOV-03 | completed ownership matrix and demo command sheet | every critical control has one accountable owner and a reachable backup; abort/fallback drill passes | PENDING |
| DOC-01 | documentation reconciliation record | production variables, alerting, rollback and capability claims agree; superseded reports are clearly labelled | PENDING |

## Evidence generation and custody

Local preflight reference: `SAFETY_PILOT_LOCAL_RENDERED_REHEARSALS_2026-08-02.md` records two same-source rendered critical-story runs, four audited control-snapshot hashes, the final recovery reset, the current 14-ledger Production-shaped pass and a final five-surface Safety Manager rendered smoke with responsive/console checks. It supports continued delivery work but does not replace any `PENDING` immutable-bundle entry above.

Run `tools/collect-safety-pilot-evidence.sh` from the repository root to create a redacted static/runtime skeleton. Its default behavior is read-only toward the application and does not run tests. `--run-tests` runs bounded non-database contract/UI tests plus the frontend build; database suites remain an exact-SHA CI gate. Use `--runtime-url` only for the named rehearsal environment.

Example:

```bash
tools/collect-safety-pilot-evidence.sh \
  --output artifacts/release-evidence/safety-RC1 \
  --runtime-url https://REHEARSAL-ENVIRONMENT \
  --external-ops /secure/staging/safety-ops-evidence \
  --run-tests
```

The optional external bundle must comply with [the external operations evidence contract](SAFETY_PILOT_EXTERNAL_OPS_EVIDENCE_CONTRACT.md). The validator checks exact-candidate binding, target-environment metadata, required exercise outcomes and referenced-file hashes. Imported reports are recorded as `REVIEW_REQUIRED`, never automatically `PASS`; custody, source authenticity and named approvals remain human release decisions.

Without `--external-ops`, the collector explicitly records `OPS-01`, `DR-01`, `REL-01` and `DATA-02` as `NOT_EVIDENCED`. It also leaves human/browser and approval gates unevidenced. Do not edit generated command output. Add new artifacts through the validated import and record their hash.

The generated `evidence-index.sha256` covers every regular file in the generated bundle except itself. After adding manual evidence, regenerate a complete external custody hash and have the evidence custodian sign/store it outside the mutable deployment host.

## Required automated suites

The exact-SHA CI run remains authoritative. At minimum its coverage must include:

- Safety incidents, coaching/scorecard, DVIR/HOS/ELD, Platform Safety controls and UI source/contract tests;
- CSRF/authentication, permission, entitlement, tenant isolation, branch isolation and Driver-self boundaries;
- Stage 47 plus Stages 65–75 clean migration, RLS policy/role reconciliation and migration idempotency; Stage 47 evidence must distinguish its always-required detention evidence/offboarding core from conditional charge/outbox integrations, and Stage 75 evidence must show support access remains default-off for the Safety pilot;
- frontend type/build/lint and production container builds;
- dependency and container vulnerability policy;
- backup/restore tooling syntax and evidence-collector syntax.
- evidence-collector secret/non-overwrite regression, external operations artifact/candidate/tamper regression, DR cleanup/partial-evidence regression and Safety restored-database verifier failure/success behavior.

Passing source-string tests alone is insufficient for runtime behavior. Passing handler tests alone is insufficient for browser authentication/CSRF, route composition or rendered state. A synthetic evidence pointer and caller-supplied hash prove metadata persistence only; they do not prove object upload, retrieval authorization, hash verification, malware scanning, retention, legal hold or custody.

## Handling contradictory evidence

Record the contradiction in `exceptions.md`, mark the gate `FAIL`, and resolve it with a new candidate or a signed exception. Do not delete the failing artifact or substitute a narrower test. An exception must name impact, affected customer promise, compensating control, accountable owner, expiry and rollback trigger.
