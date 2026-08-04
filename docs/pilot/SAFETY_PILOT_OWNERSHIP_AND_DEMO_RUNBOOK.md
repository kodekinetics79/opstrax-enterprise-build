# Safety pilot control ownership and professional demo runbook

Status: template. A name, reachable backup and evidence reference are required before GO; a team name alone is insufficient.

## Control ownership

| Control/outcome | Accountable | Responsible operator | Consulted | Informed | Backup / evidence |
|---|---|---|---|---|---|
| Candidate freeze, provenance and GO/NO-GO | CTO | Release owner | Security, QA, Product | Sales, Support | PENDING |
| Data classification, retention, export/delete and client-data approval | CIO/privacy owner | Data steward | Legal, Security | Client owner | PENDING |
| Tenant lifecycle, package policy, entitlements and market packs | Platform product owner | Platform Admin operator | Sales, Security | Demo conductor | PENDING |
| Demo seed, simulator and synthetic-data environment controls | SRE/release owner | Deployment operator | QA, Security | Demo conductor | PENDING |
| Tenant RBAC, personas and branch bindings | Customer/tenant admin owner | Fixture operator | Fleet Safety SME, QA | Demo conductor | PENDING |
| Safety workflow correctness and claims | Fleet Safety SME | Safety product owner | Compliance/legal, QA | Sales | PENDING |
| Application/system/owner DB identity and RLS | Security owner | Platform/SRE operator | DBA, CTO | QA | PENDING |
| Migrations and schema compatibility | Database owner | Release engineer | API owner, SRE | Incident commander | PENDING |
| External monitoring, alert delivery and support response | SRE/support owner | On-call operator | API/provider owners | CTO/CIO/Sales | PENDING |
| PITR, evidence-object recovery and rollback | Incident commander | DBA/SRE operator | Security, data steward | Client owner | PENDING |
| Fixture reset and evidence custody | QA owner | UAT evidence custodian | Product, Security | Approvers | PENDING |
| Client narrative, scope disclaimer and fallback story | Product owner | Sales demo conductor | CTO, Fleet Safety SME | Client attendees | PENDING |

One accountable owner must have decision authority for each row. The same person may fill multiple roles for a small pilot, but responsibility and backup cannot be implicit.

## Client-ready run of show

Target a 30–40 minute demonstration and rehearse the exact sequence twice.

| Minute | Story | Persona | Proof | Truthful fallback |
|---|---|---|---|---|
| 0–3 | Scope, supported capabilities and exclusions | Demo conductor | signed capability statement | static architecture/capability slide |
| 3–7 | Platform control boundary | Platform Admin | tenant status and approved module snapshot | pre-captured redacted control snapshot |
| 7–14 | Incident to external evidence-reference metadata and response | Safety Manager | persisted external-reference metadata and incident/evidence/audit IDs; explicitly no upload, retrieval or verification claim | seeded incident is fallback context only; its telemetry metadata is synthetic, URL-free, not verified, not retrievable and not custody-managed |
| 14–20 | Coaching and explainable score | Safety Manager → Driver | source/formula/window and Driver acknowledgement | read-only score provenance and completed task |
| 20–27 | DVIR defect to repair/acknowledgement | Driver → Maintenance → Driver | persisted state, OOS effect and audit | read-only completed fixture; state why mutation is paused |
| 27–32 | HOS/ELD honest state | Driver → Safety Manager | certification snapshot and degraded/provider state | read-only certification; explicit non-certified-ELD disclaimer |
| 32–36 | Cross-role/branch controls | Limited persona | navigation plus direct deep-link/API denial | recorded rehearsal evidence, never expose another tenant’s data |
| 36–40 | Outcomes, pilot measures, support and next step | Product/Sales | scorecard and support/RPO/RTO commitments | approved pilot brief |

## Demo command rules

- The conductor narrates; a separate operator drives when possible; the technical owner watches health/logs without screen sharing sensitive consoles.
- Use named synthetic personas and clearly identify seeded data. Never call simulated data live client data.
- Never present fixture v7's synthetic incident metadata/hash as an uploaded object,
  retrievable evidence, independent verification or chain-of-custody proof.
- Keep browser zoom, viewport, notification state and test tenant fixed. Close unrelated tabs and disable personal notifications.
- Pre-stage every persona at a safe landing page; never reveal passwords, tokens, cookies, connection strings or internal customer records.
- Record created business IDs and correlation IDs in the evidence sheet, not in chat or slides.
- Do not improvise a configuration change, entitlement, database edit, seed, provider credential or privileged impersonation during the client session.
- A fallback must be rehearsed, current and labelled as pre-captured/read-only. A screenshot is not represented as a live transaction.

## Abort and fallback authority

The technical owner calls **STOP** for any possible tenant/branch/persona leak, unexpected production/customer data, integrity error, wrong environment/version, security warning, unplanned privileged access, or repeated mutation. The demo conductor immediately stops screen sharing and moves to the approved non-product discussion. Sales cannot override a technical stop.

For a single non-security UI/API failure, the operator may retry once only if the action is idempotent and the runbook explicitly permits it. Otherwise use the labelled fallback and open a defect. Two failures in the same critical story, any P0/P1, or loss of health/monitoring ends the product demonstration.

## Pre-client professionalism check

- [ ] Attendee roles, objective, timebox, recording consent and question owner confirmed.
- [ ] Capability/exclusion statement and pilot success measures approved by Product and Fleet Safety SME.
- [ ] Environment banner/tenant/persona is obvious; no personal or unrelated client data is present.
- [ ] Two complete rehearsals passed on the exact candidate; fallback artifacts match the same version.
- [ ] Demo conductor, operator, technical owner, on-call, rollback authority and backups are present/reachable.
- [ ] Control snapshot, health, external monitor and evidence capture are green immediately before screen share.
- [ ] Follow-up owner records questions, commitments and defects without promising unapproved scope or dates.
