# Safety pilot external operations evidence contract

Status: tooling and custody preflight implemented; target-environment exercises and approvals remain **PENDING**.

This contract covers the four release gates that cannot be proven by a local build: monitoring and alert delivery (`OPS-01`), point-in-time recovery (`DR-01`), deploy/config rollback (`REL-01`), and retention/privacy operations (`DATA-02`). A JSON report is not proof by itself. Each report must bind to provider exports or other independently reviewable source artifacts by SHA-256, identify the exact candidate and target environment, and receive named approval.

## Required bundle

Place these four reports and their sanitized source artifacts in one directory:

| Report | Required exercise |
|---|---|
| `ops-01-monitor-alert.json` | Inject a synthetic service failure; prove dashboard state, external delivery, acknowledgement by the primary or backup on-call, correlation trace and recovery within the approved threshold. |
| `dr-01-pitr-restore.json` | Restore to the requested point, boot the exact candidate with restricted identities, validate tenant/branch boundaries and Safety records, retrieve/hash a separately scoped generic managed document/POD test object when required by the signed contract, include cutover validation in RTO, and verify throwaway-resource cleanup. That object check does not prove Incident evidence custody. |
| `rel-01-rollback.json` | Exercise write freeze/resume and rollback from immutable candidate digests to immutable known-good digests; prove schema/config compatibility, Production health, tenant isolation, one synthetic Safety mutation and alert recovery. |
| `data-02-retention-privacy.json` | Record approved numeric policy, healthy worker, expired-row purge, legal-hold prevention, subject export, subject delete or a formally approved product exception, implementation-scope review/exception, and object-store retention/recovery against the signed privacy agreement. The current worker enforces only location events, notifications and report-execution logs; numeric audit/security policy values are not proof those categories were purged. |

Every report uses `schema_version: 1` and must contain `gate_id`, exact `candidate_sha`, `outcome: "PASS"`, non-local `environment`, unique `run_id`, distinct named `operator` and `approver`, UTC start/end/approval timestamps, at least one HTTPS external reference, and one or more `source_artifacts`. Monitoring events must fall inside the declared run window; reported alert delivery is derived from its event timestamps; restore age is derived from the run start and restore target; and PITR/rollback durations must match the declared run window. A result outside an accepted RPO, RTO or rollback threshold is rejected. Each source artifact entry is `{ "path": "relative/path", "sha256": "..." }`. Paths must remain inside the bundle. Do not include tokens, cookies, private keys, database URLs, personal data or raw database extracts.

The gate-specific field contract is executable in `tools/validate-safety-pilot-external-ops-evidence.sh`; keep the report close to the raw dashboard/provider/deployment export instead of transcribing results into prose alone.

## Validation and collection

Run against the frozen candidate:

```bash
tools/validate-safety-pilot-external-ops-evidence.sh \
  --bundle /secure/staging/safety-ops-evidence \
  --candidate "$(git rev-parse HEAD)" \
  --output /secure/staging/external-ops-validation.tsv

tools/collect-safety-pilot-evidence.sh \
  --output artifacts/release-evidence/safety-RC1 \
  --runtime-url https://REHEARSAL-ENVIRONMENT \
  --external-ops /secure/staging/safety-ops-evidence
```

The validator rejects candidate mismatch, local/development environment names, unsafe paths, symlinks, missing or tampered source artifacts, invalid thresholds, incomplete gate-specific assertions and several high-confidence secret forms. The collector imports a validated bundle with restrictive file permissions and hashes every file in its custody index.

Successful tool validation produces `CONTRACT_VALID_REVIEW_REQUIRED`, and the collector records `REVIEW_REQUIRED`. It deliberately does not record `PASS`: the evidence custodian must authenticate source exports and operator/approver identity, verify the environment and timestamps, sign the final custody hash outside the mutable deployment host, and then update the release decision record. Locally generated fixtures and mocked regression tests never satisfy these gates.

## DR phase distinction

`tools/dr-restore-drill.sh` can write `DR_DATABASE_EVIDENCE_OUTPUT`. That artifact is explicitly `DATABASE_PITR_PHASE_ONLY` / `PARTIAL`; it measures restoration to a queryable database and records whether the Safety database contract ran. It excludes restricted application boot, tenant/branch application isolation, separately scoped generic managed-object recovery, alert delivery and cutover time. It is an input to `DR-01`, not the final `dr-01-pitr-restore.json`. Incident evidence is external-reference metadata and is never validated as an application-managed object by this contract.
