# G2B certified ELD partner decision scorecard

**Owning gate:** G2B / GitHub Issue #116  
**Capability truth:** Certified ELD/HOS **ROADMAP**; current HOS structures **DEVELOPMENT**  
**Decision state:** no provider, device, firmware/software boundary or jurisdiction is selected by this document

This instrument prepares the Stage 1 partner decision. It does not certify a provider, convert a GPS tracker into an ELD or activate G3A HOS.

## 1. Complete one lane per jurisdiction

Do not combine U.S. and Canadian evidence into one score. A candidate that serves both jurisdictions requires two complete evidence lanes and may receive different decisions.

| Decision field | U.S. lane | Canada lane |
|---|---|---|
| Intended geography and customer segment | | |
| Provider legal identity | | |
| Exact product/device/app | | |
| Hardware revision | | |
| Firmware/software version | | |
| Regulatory identifier | FMCSA ELD identifier | Transport Canada certification number |
| Current official status and capture date | | |
| Revoked/removed history checked | | |
| Certification authority/model | Provider self-certification/registration | Accredited third-party certification body |
| Sandbox/account path | | |
| Commercial integration rights owner | | |
| Decision owner and date | | |

## 2. Hard gates

A candidate is disqualified or remains HOLD if any applicable hard gate is not proven.

| Hard gate | Required evidence | U.S. | Canada |
|---|---|---|---|
| Exact identity | Provider, product, model, hardware and firmware/software boundary | | |
| Current regulatory status | Dated official-list capture with exact identifiers | | |
| Revocation/removal check | Dated official revoked/removed-list evidence | | |
| Commercial authority | Written API/integration, data-processing, support and distribution rights | | |
| Authentic access | Authorized sandbox plus production/pilot credential path | | |
| Required ELD workflows | Provider evidence for applicable HOS, edits, unidentified driving, certification, diagnostics/malfunctions and inspection/transfer | | |
| Security/privacy | Accepted data flow, residency, retention, token, audit, incident and subprocessor boundaries | | |
| Supportability | Hardware availability, installation, replacement/RMA, version/change notice and escalation path | | |
| Independent review | Two independent qualified ELD/HOS regulatory perspectives plus Security, Principal SDET and Fleet Product acceptance; no implementer self-certification | | |

Official status is necessary but not sufficient: the OpsTrax integration and customer workflow must still pass Stage 2 on the selected exact boundary.

## 3. Weighted assessment

Score each evidence-backed item from 0 to 5. `0` means missing or unacceptable; `3` means meets the documented pilot need; `5` means independently proven with strong contractual and technical evidence. Unsupported claims score `0`. For each row, `weighted result = (score ÷ 5) × weight`; the jurisdiction total is the sum of its seven weighted results and therefore ranges from 0 to 100.

Complete the U.S. and Canada tables independently. Do not copy a score or total between them.

### 3A. U.S. assessment

| Domain | Weight | U.S.-specific evidence questions | Score (0–5) | Weighted result |
|---|---:|---|---:|---:|
| Regulatory identity and change control | 20 | Exact FMCSA-listed boundary? Current registered and revoked-list status? Version/removal notice? | | |
| HOS/ELD workflow completeness | 20 | Applicable U.S. driving detection, edits/annotations, unidentified driving, certification, exemptions, diagnostics/malfunctions, inspection and transfer? | | |
| API and integration depth | 15 | Versioned API, scopes, webhooks/polling, backfill, rate limits, identities, audit fields and sandbox parity? | | |
| Security, privacy and residency | 15 | Least privilege, token lifecycle, encryption, audit, retention/deletion, subprocessors and incident notification? | | |
| Commercial rights and economics | 10 | Written U.S. integration/resale rights, pricing, minimums, term, customer ownership, support and exit rights? | | |
| Hardware and field operations | 10 | U.S. availability, install, vehicle compatibility, connectivity, replacements/RMA, firmware control and coverage? | | |
| Reliability and support | 10 | SLA/SLO, status/incident process, retry/recovery expectations, escalation, documentation and change notice? | | |
| **U.S. total** | **100** | Hard gates still control regardless of score | | |

### 3B. Canada assessment

| Domain | Weight | Canada-specific evidence questions | Score (0–5) | Weighted result |
|---|---:|---|---:|---:|
| Regulatory identity and change control | 20 | Exact Transport Canada certified boundary, certification number/body and technical-standard version? Current and revoked status? | | |
| HOS/ELD workflow completeness | 20 | Applicable Canadian driving detection, edits/annotations, unidentified driving, certification, exemptions, diagnostics/malfunctions, inspection and transfer? | | |
| API and integration depth | 15 | Versioned API, scopes, regional hosting, webhooks/polling, backfill, rate limits, identities, audit fields and sandbox parity? | | |
| Security, privacy and residency | 15 | Least privilege, token lifecycle, encryption, audit, Canadian/cross-border residency, retention/deletion, subprocessors and incident notification? | | |
| Commercial rights and economics | 10 | Written Canadian integration/resale rights, pricing, minimums, term, customer ownership, support and exit rights? | | |
| Hardware and field operations | 10 | Canadian availability, install, vehicle compatibility, connectivity, replacements/RMA, firmware control and coverage? | | |
| Reliability and support | 10 | SLA/SLO, status/incident process, retry/recovery expectations, escalation, documentation and certification-change notice? | | |
| **Canada total** | **100** | Hard gates still control regardless of score | | |

Decision guidance:

- **80–100 and every hard gate passed:** eligible for CTO selection and Stage 2 planning; not yet certified.
- **65–79 and every hard gate passed:** conditional shortlist; gaps require dated owners before selection.
- **Below 65 or any hard gate missing:** HOLD or exclude.

The CTO may reject a high-scoring candidate for a material risk. The score cannot waive a hard gate.

## 4. Provider questionnaire

Request evidence rather than yes/no marketing answers.

### Regulatory and product identity

1. Identify the legal provider and exact device/app, hardware revision, firmware/software version and regulatory identifier proposed for each jurisdiction.
2. Supply the current certification/registration statement, data-transfer methods and notification obligations for version, certification or listing changes.
3. Describe how removed/revoked status and customer migration are handled.

### API and data

1. Supply current API/OpenAPI documentation, authentication method, scopes, regional base URLs and sandbox limitations.
2. Detail driver, vehicle, device and ELD identity keys and their lifecycle.
3. Detail duty-status history, edits, annotations, certification, unidentified driving, exemptions, malfunctions/diagnostics and inspection/transfer data available through the integration.
4. State pagination, webhook delivery, polling, backfill windows, rate limits, retry guidance, retention and correction behavior.
5. Explain breaking-change/version policy and advance notice.

### Commercial and operational

1. Confirm in writing the rights to integrate, process, display, retain, support and, if applicable, resell the data/service.
2. Supply pricing, minimums, contract term, pilot/sandbox terms, support/SLA, incident notification and exit/data-return terms.
3. Supply supported geographies, hardware lead times, installation requirements, warranty, replacement/RMA and escalation process.

### Security and privacy

1. Supply security and privacy documentation, data-flow/residency map, subprocessors, retention/deletion behavior and incident-notification terms.
2. Detail credential issuance, least-privilege scopes, rotation/revocation, auditability and tenant/customer isolation.
3. Supply current independent assurance reports available under NDA and disclose material exceptions relevant to the service.

## 5. Evidence register

| Evidence ID | Jurisdiction | Candidate boundary | Source/owner | Capture date | Restricted artifact reference + hash | Independent verifier 1 | Independent verifier 2 (P0/regulatory) | Result/gap |
|---|---|---|---|---|---|---|---|---|
| G2B-001 | | | | | | | | |

Official-list captures must include the exact decision date and identifiers. Links alone are insufficient because status may change.

### Restricted evidence handling

- Store contracts, DPAs, security reports, pricing, credentials, customer/provider contacts and unredacted regulatory correspondence only in the approved access-controlled evidence repository.
- Git, GitHub issues and pull requests may contain the evidence ID, owner, classification, capture date, approved redacted extract, immutable hash and restricted-location reference; they must not contain the confidential artifact or secret.
- Apply least privilege, record access/retention owner and expiry, and verify redaction before any extract is published.
- Never paste credentials, production data, personal information, NDA material or unredacted commercial terms into this scorecard.

### Appendix B independent sign-off roster

P0 regulatory/provider claims require two independent qualified-human perspectives. The implementer cannot fill either independent perspective, and one person cannot fill both. AI review is supporting assurance only.

| Required role | U.S. named reviewer / decision / date | Canada named reviewer / decision / date |
|---|---|---|
| Independent ELD/HOS regulatory perspective 1 | | |
| Independent ELD/HOS regulatory perspective 2 | | |
| Security/privacy reviewer | | |
| Principal SDET | | |
| Fleet Product reviewer | | |
| Provider Integration SME | | |
| Commercial/Legal owner | | |
| CTO/program owner | | |

Where a role is not applicable, the CTO and both regulatory perspectives must record the jurisdiction-specific reason; “not available” is not an N/A justification.

## 6. Exclusion and selection record

For every evaluated candidate record:

- hard-gate result and weighted score;
- material exclusions and unresolved assumptions;
- whether the candidate is excluded, shortlisted, conditionally selected or selected for Stage 2;
- exact jurisdiction/device/firmware/software/commercial boundary;
- decision owner, date and required follow-up evidence;
- explicit statement that selection is not OpsTrax certification.

No candidate may enter Stage 2 until the official regulatory identity, commercial rights and authentic account/device path are recorded. No Stage 2 result may promote Certified ELD/HOS until the applicable end-to-end evidence and mandatory independent acceptance pass.
