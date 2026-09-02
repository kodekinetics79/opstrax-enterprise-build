# G2B ELD regulatory and partner-readiness ledger

**Owning gate:** G2B / GitHub Issue #116
**Activation:** `CR-2026-09-02-01`
**Capability truth:** Certified ELD/HOS **ROADMAP**; existing HOS structures **DEVELOPMENT**
**Gate decision:** **HOLD — readiness research only; no provider, device, jurisdiction or certification has been selected or accepted**

## Commercial-truth boundary

- OpsTrax is not an ELD manufacturer/provider and has no OpsTrax-certified ELD today.
- A GPS tracker, telematics adapter, schema, HOS screen, mock, photograph or provider name is not ELD certification evidence.
- U.S. and Canadian certification models are different and must remain separate in evidence, contract language and release decisions.
- A provider/device listing is necessary evidence for the applicable jurisdiction but does not certify the OpsTrax integration or its customer workflow.

## United States path

The official FMCSA ELD site describes the registered list as devices **self-certified by providers**. The official list states that listed devices are self-certified by the manufacturer and that FMCSA does not endorse any ELD. FMCSA also maintains a revoked list and may remove a model/version that does not meet the rule.

Official sources (URLs reviewed on 2026-09-02; official status must be recaptured on the actual candidate decision date):

- [FMCSA ELD portal and registered/self-certified list](https://eld.fmcsa.dot.gov/)
- [FMCSA registered device list](https://eld.fmcsa.dot.gov/List/GetListOfELDs?listType=Registered)
- [FMCSA ELD registration and certification FAQs](https://www.fmcsa.dot.gov/hours-service/elds/eld-registration-and-certification-faqs)
- [49 CFR 395.22 — motor carrier responsibilities](https://www.ecfr.gov/current/title-49/subtitle-B/chapter-III/subchapter-B/part-395/subpart-B/section-395.22)
- [FMCSA ELD functions FAQs](https://www.fmcsa.dot.gov/hours-service/elds/eld-functions-faqs)
- [FMCSA ELD news and status notices](https://eld.fmcsa.dot.gov/support/newsandevents)

Required selection evidence for an exact U.S. candidate:

1. Provider legal identity and current commercial relationship.
2. Exact device name, model number, software/firmware version, ELD identifier and FMCSA registration identity.
3. Current registered status plus a revoked-list check captured on the decision date.
4. Provider certification statement, supported data-transfer methods, malfunction behavior and applicable 49 CFR Part 395 boundary.
5. API/integration rights, data-processing terms, support/SLA, incident notification and change/version obligations.
6. End-to-end evidence for driving detection, edits, annotations, certification, unidentified driving, personal conveyance, yard move, clocks/cycles, malfunctions/diagnostics, roadside inspection and transfer.

No item above is currently recorded for a selected partner/device; therefore the U.S. path remains **HOLD**.

## Canada path

Transport Canada states that both ELD hardware and software/app must be tested and certified by a third-party certification body accredited by the Minister of Transport. The official device list identifies certified and revoked records and states that the certification body retains authority over a model's certification status.

Official sources (URLs reviewed on 2026-09-02; official status must be recaptured on the actual candidate decision date):

- [Transport Canada ELD program](https://tc.canada.ca/en/road-transportation/electronic-logging-devices)
- [Transport Canada certification of ELDs](https://tc.canada.ca/en/road-transportation/electronic-logging-devices/certification-electronic-logging-devices)
- [Transport Canada certified and revoked device list](https://tc.canada.ca/en/road-transportation/electronic-logging-devices/list-electronic-logging-devices)
- [Transport Canada accredited certification-body registry](https://tc.canada.ca/en/road-transportation/electronic-logging-devices/registry-accredited-certification-bodies)
- [Commercial Vehicle Drivers Hours of Service Regulations, section 79.2](https://laws-lois.justice.gc.ca/eng/regulations/SOR-2005-313/section-79.2.html)

Required selection evidence for an exact Canadian candidate:

1. Provider legal identity and current commercial relationship.
2. Exact product/device/app boundary, ELD identifier, certification number, technical-standard version and certification body.
3. Current active status plus revoked-history and certification-body confirmation captured on the decision date.
4. Applicable Canadian HOS behavior, malfunction/diagnostic behavior, engine synchronization, supported transfer protocols and inspection workflow.
5. API/integration rights, data-processing terms, support/SLA, incident notification and certification/version change obligations.
6. End-to-end OpsTrax integration and customer-workflow evidence for the selected certified boundary.

No item above is currently recorded for a selected partner/device; therefore the Canadian path remains **HOLD**.

## Partner due-diligence evidence pack

Before a candidate may move from research to selection, the owning issue must contain:

| Evidence domain | Required artifact | Current status |
|---|---|---|
| Regulatory identity | Dated official-list capture and exact provider/device/software/certification identifiers | MISSING |
| Regulatory interpretation | Independent U.S. and, if applicable, Canadian ELD/HOS SME review | MISSING |
| Commercial authority | Signed/approved API, resale/integration, data-processing and support rights | MISSING |
| Provider access | Authorized non-production and production account/token paths | MISSING |
| Security/privacy | Data-flow, threat model, secrets, retention, cross-border/privacy and incident obligations | MISSING |
| Product workflow | Driver, fleet administrator, safety/compliance and roadside-inspection journeys | MISSING |
| Technical integration | Versioned API contract, canonical identity/lineage, retry/idempotency, audit and recovery evidence | MISSING |
| Field evidence | Exact device/vehicle/app workflow with authentic records and transfer behavior | MISSING |
| Independent acceptance | Appendix B two-perspective P0 review plus SDET/Security/Fleet Product acceptance | MISSING |

The jurisdiction-separated hard gates, weighted assessment, provider questionnaire, evidence register and exclusion/selection record are prepared in `G2B_ELD_PARTNER_DECISION_SCORECARD.md`. The instrument is ready for candidate evidence; its existence is not a provider selection or certification result.

## Decision rule

Repository code, research and AI-assisted reviews may prepare the decision packet, but they are not qualified-human regulatory approvals. The implementer may not self-certify. No candidate may be called “certified,” no regulated package may be sold, and G3A HOS may not activate until the exact partner/device/jurisdiction boundary, commercial rights, official status and mandatory independent acceptance exist.
