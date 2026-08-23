# OpsTrax Defect Register — Delta

Run: `RETEST-20260821-1035-R1` · **The preserved register at `docs/uat/UAT-20260821-1035/OPSTRAX_DEFECT_REGISTER.md` is authoritative evidence and has NOT been modified.** This delta records status changes and new findings.

Status vocabulary: FIXED / LIVE RETEST PASS · PARTIALLY FIXED · STILL FAILING · BLOCKED · NOT APPLICABLE TO DEPLOYED SHA.
**No defect below is marked "LIVE RETEST PASS" — Gate 7 has not run.** Code-complete repairs proven on the isolated environment are marked REPAIRED (ISOLATED PROOF) pending live verification.

## Status changes

| ID | Sev | Was | Now | Basis |
|---|---|---|---|---|
| DEF-002 | High | OPEN/BLOCKING | **PARTIALLY FIXED** | DB-dependent lanes now execute: backend DB 410/410, RLS 9/9, telematics durability **8/8** (incl. an encryption assertion that had never once run). Object storage + deployed-edge truth remain unverified. |
| DEF-003 | Critical | OPEN | **NOT APPLICABLE TO DEPLOYED SHA** + REPAIRED (ISOLATED PROOF) | `telematics/deploy/**` does not exist at `979c142` (verified `git cat-file`). Installer now fails closed; example allowlist admits zero entries through the real parser. |
| DEF-004 | High | OPEN | **SPLIT** — 004a NOT APPLICABLE TO DEPLOYED SHA (repaired); **004b CONFIRMED LIVE** (repaired) | argv-secret path is HEAD-only. The manifest half **is** live at `979c142`: `render.yaml:38/40` provisioned keys whose presence is startup-fatal. Keys removed; validator untouched. |
| DEF-005 | High | OPEN | **NOT APPLICABLE TO DEPLOYED SHA** + REPAIRED (ISOLATED PROOF) | `FileForwardOutbox.cs` absent at `979c142`. Now AES-256-GCM with AAD binding, 0600/0700 modes; byte-level test proves no plaintext IMEI/coordinate in UTF-8, UTF-16LE, or 4-dp decimal form. |
| DEF-006 | High | OPEN | **REPAIRED (ISOLATED PROOF)** | Timeline/Recommendation/SimpleAction factories gated; ~70 further internal endpoints gated; 4 cross-tenant maintenance reads company-scoped; `EntityInAuthorizedScope` ownership check added; gate-coverage source contract with a 26-entry public allowlist. *Wave C auditing the allowlist.* |
| DEF-015 | High | OPEN/BLOCKING | **REPAIRED (ISOLATED PROOF)** | Display policy = **masked last-four** (`•••• 1234`, or `Unavailable`). Applied at roster, CSV export, detail, scorecards, reporting dataset. Plaintext exits only via DSAR. Found and fixed en route: the workforce projection shipped the license **number** mislabeled as `licence_class`. |
| DEF-016 | Critical | OPEN/BLOCKING | **PARTIALLY FIXED** | Schema proven on isolated DB; **staging still requires the migration run** at Gate 6. |
| DEF-017 / 018 / 019 | High/Crit | OPEN/BLOCKING | **PARTIALLY FIXED — ROOT CAUSE RECLASSIFIED** | Not drift: **no migration ever existed** for these columns. stage86 creates all 8 + enrolls them in the readiness contract. Staging apply pending. |
| DEF-020 | High | OPEN/BLOCKING | **PARTIALLY FIXED** | Divergence explained: count query succeeded on base columns, list query 42703'd on `module_key`/`severity` and rendered zero. stage86 supplies the columns; AuditLogsPage now renders an explicit error state — a zero is always a *measured* zero. |
| DEF-021 | Medium | OPEN | **REPAIRED (ISOLATED PROOF)** | `role_id` now supplied at all four provisioning inserts + stage87 backfill. The Active-only role-card filter is correct and was deliberately left unchanged. |
| DEF-022 | High | OPEN/BLOCKING | **REPAIRED (ISOLATED PROOF)** | Confirmed cause: `toUtcIso` threw *inside* the `mutate(...)` argument with no try/catch — the throw escaped before any network call, and the error slot stayed null because the mutation never ran. Conversions hoisted; all silent returns now visible errors. |
| DEF-023 | High | OPEN | **REPAIRED (ISOLATED PROOF)** | Frontend: accessible `ConfirmDialog` replaces `window.confirm`. Server: revoke moved to a tenant transaction, appends the state-transition ledger row it previously skipped, clears all 7 credential columns. |
| DEF-024 | High | OPEN/BLOCKING | **REPAIRED (ISOLATED PROOF)** | Cause: alias closure made `customer_portal:view` sessions look dashboard-capable, so the portal branch was dead code. Identity-boundary-first ladder + dedicated `CustomerLayout` outside the internal shell + bounce. |
| DEF-025 | High | OPEN/BLOCKING | **REPAIRED (ISOLATED PROOF)** | `/admin` and `/user-management` now require **direct** exact-match grants; the `reports.manage → audit:view` alias edge removed. |
| DEF-026 | Critical | OPEN/BLOCKING | **PARTIALLY FIXED** | Two causes: missing schema (stage84, staging apply pending) **and** no degradation. Code now degrades on 42P01/42501/42703. Also closed a fail-open: inactive/suspended drivers retained portal access. |
| DEF-027 | High | OPEN/BLOCKING | **REPAIRED (ISOLATED PROOF)** | Not a schema defect. Missing `deleted_at` filters, no binding validation, and a dangling binding silently rendering an empty portal instead of denying. Now fail-closed 403, binding validated + audited, admin UI field added. |
| DEF-028 | — | (proposed) | **NOT OPENED — UNCONFIRMED** | See below. |

## DEF-028 (Cloudflare) — deliberately NOT opened

The brief instructed opening it *only after confirming it from preserved evidence*. Full-text search of `docs/uat/UAT-20260821-1035/` returns **zero** matches for Cloudflare/challenge/equivalent: the register ends at DEF-027, the 13-row ledger has no such row, and none of the 4 screenshots covers it. The only Cloudflare strings in the repo are R2 object-storage config. Recording it would fabricate evidence. It stays a candidate pending a live probe at Gate 7 — and if a challenge appears there, the response is to stop automation, preserve state, mark checks BLOCKED, and request an approved staging-only access decision. **No bypass or weakening of Cloudflare will be attempted.**

## New findings opened this run

| ID | Sev | Finding | Status |
|---|---|---|---|
| NEW-R1-01 | High | **Structural**: 8 runtime-only columns had no migration and could never exist in any protected environment (root cause of DEF-017/018/019/020) | REPAIRED (stage86 + readiness enrolment) |
| NEW-R1-02 | Critical | **Fail-open**: `user["roleName"] ?? "Company Admin"` granted a **wildcard admin session** to any user with a NULL/blank role name, at 4 sites | REPAIRED — fails closed to zero permissions. **Isolated-DB audit: 0 affected rows. Staging/production need the same audit before deploy.** |
| NEW-R1-03 | High | **Deploy-order landmine**: any SHA ≥ `de00b75` fails `/health/ready` unless stage83ps/84/85/86/87 are applied first; `render.yaml` gates traffic on it | OPEN CONSTRAINT — enforced at Gate 6 |
| NEW-R1-04 | High | **Packaging drift**: `backend-dotnet/Dockerfile` per-file COPY list silently stopped at stage82, omitting stage83-87 from the image | REPAIRED (directory COPY); root `Dockerfile` still affected |
| NEW-R1-05 | Medium | 25 migrations enrolled in no applier; 83 runtime-only columns remain split-brain | FENCED — named and test-enforced, shrink-only |
| NEW-R1-06 | Medium | Role definitions disagree across 3 sources (backend defaults, DB seed, frontend mirror) with name drift | OPEN — documented in the role catalogue, not attempted this run |
| NEW-R1-07 | Medium | Cross-tenant reads: `/api/maintenance` list/detail/due-soon/overdue read across **all** tenants | REPAIRED (company-scoped) |
| NEW-R1-08 | Low | Stale docs still instruct the removed `--secret` flag (fail-safe: following them exits 2) | OPEN — Gate 4 |
| NEW-R1-09 | Medium | `Reseller`/`Partner Admin` backend roles carry unaudited wildcard `["*"]` | OPEN — flagged, out of scope |
