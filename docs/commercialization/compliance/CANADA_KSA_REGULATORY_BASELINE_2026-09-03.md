# OpsTrax Canada + Saudi Arabia Regulatory Baseline

**Program:** Immediate commercial-pilot hardening  
**Markets:** Canada and Kingdom of Saudi Arabia  
**Baseline date:** 2026-09-03  
**Owners:** CTO / Regulatory SMEs / Fleet Product / Security & Privacy / Principal SDET  
**Commercial truth:** **COMPLIANCE-READY / INTEGRATION-READY ONLY** until the exact external evidence gates below close.

## Executive decision

Canada and KSA are the active regulatory commercialization lanes. USA remains supported architecturally but is not the immediate certification priority.

No OpsTrax screen, schema, seeded record, tracker, API name, partner application, or test fixture is certification evidence.

- Canada: the regulated ELD boundary must be an exact currently active Transport Canada-certified hardware/software/app combination, verified on the decision and release dates.
- KSA: where the customer's transport activity requires automated tracking linkage, the regulated boundary must use the applicable TGA-qualified provider/device/authority path unless OpsTrax itself has separately obtained the necessary Saudi registration/qualification.

## 1. Canada baseline

### Official federal sources

1. Commercial Vehicle Drivers Hours of Service Regulations, SOR/2005-313  
   https://laws-lois.justice.gc.ca/eng/regulations/SOR-2005-313/FullText.html
2. Transport Canada ELD program  
   https://tc.canada.ca/en/road-transportation/electronic-logging-devices
3. ELD certification program  
   https://tc.canada.ca/en/road-transportation/electronic-logging-devices/certification-electronic-logging-devices
4. Certified/revoked ELD list  
   https://tc.canada.ca/en/road-transportation/electronic-logging-devices/list-electronic-logging-devices
5. Accredited certification bodies  
   https://tc.canada.ca/en/road-transportation/electronic-logging-devices/registry-accredited-certification-bodies

### Product rules — south of 60°N

The baseline rule engine must represent at least:

- 13-hour daily driving limit.
- 14-hour daily on-duty limit.
- 16-hour elapsed-time limit after the applicable qualifying off-duty period.
- At least 10 hours off duty in a day, including the applicable consecutive block/additional off-duty requirements.
- At least 24 consecutive hours off duty in the preceding 14 days before driving.
- Cycle 1: 70 hours on duty in 7 days.
- Cycle 2: 120 hours on duty in 14 days, including the 70-hour/24-consecutive-hours-off condition.
- Cycle 1 reset: 36 consecutive hours off duty.
- Cycle 2 reset: 72 consecutive hours off duty.
- Applicable switching, deferral, sleeper-berth, special-permit and exemption behavior only when the pilot scope requires it and the corresponding source/test evidence exists.

### Product rules — north of 60°N

North-of-60 rules must be a separate profile, never silently inherited from the south profile. Minimum model:

- 15-hour driving limit.
- 18-hour on-duty limit.
- 20-hour elapsed-time limit after the applicable qualifying off-duty period.
- Cycle 1: 80 hours in 7 days.
- Cycle 2: 120 hours in 14 days, including the 80-hour/24-consecutive-hours-off condition.
- 36-hour / 72-hour cycle-reset requirements as applicable.

### Canadian carrier credentials

The National Safety Code is a set of safety standards, not one generic federal "NSC carrier registration." The pilot configuration must map the customer's actual base province/territory and operating model to its applicable safety-fitness/carrier credentials, inspections, records and enforcement requirements.

### Canadian ELD certification boundary

Before Canada pilot GO, record and independently verify:

- provider legal identity;
- exact ELD product/app name;
- hardware/device model;
- ELD identifier;
- certification number;
- certification body;
- technical-standard version;
- exact software/firmware boundary;
- engine synchronization/interface;
- active status and revoked-history check;
- driver inspection/data-transfer behavior;
- API/integration/commercial rights;
- real device/account field evidence through OpsTrax.

**Current evidence status:** MISSING / selection in progress.

## 2. Saudi Arabia baseline

### Official sources

1. Transport General Authority — Truck Drivers Guidelines  
   https://tga.gov.sa/Content/Uploads/Regulations/LandRegulations/Documents/en/Truck%20Driverd%20Guideline.pdf
2. Communications, Space & Technology Commission — Tracking Services Registration  
   https://www.cst.gov.sa/en/business/services/Tracking-Services-Registration
3. Saudi Data & AI Authority — Personal Data Protection Law / regulations and cross-border transfer rules  
   https://dgp.sdaia.gov.sa/

The exact TGA regulation and qualified-provider status applicable to the customer's activity must be refreshed from official Saudi sources before release.

### TGA goods-transport driver-hours baseline

The product must model at least:

- maximum 9 driving hours in 24 hours;
- extension to 10 hours no more than twice per week;
- maximum 56 driving hours in a week;
- maximum 90 driving hours across two consecutive weeks;
- 45-minute break after a maximum of 4.5 hours continuous driving, subject to applicable split-break provisions;
- at least 11 consecutive hours daily rest;
- at least 48 consecutive hours weekly rest;
- maximum six consecutive working days;
- any safety/emergency extension only as an auditable exception with reason/evidence, never as the normal limit.

The legacy `SA-HOS-10H` / `SASO HOS` product semantics are prohibited as the governing Saudi HOS rule.

### TGA tracking/provider boundary

For the pilot customer's exact transport activity, determine whether automated tracking/authority linkage applies. Where it does:

- use an exact TGA-qualified automated tracking provider unless OpsTrax has separately obtained the required status;
- record provider identity and qualification evidence/date;
- record device model, IMEI, SIM, firmware and installation boundary;
- record authority-platform linkage/status;
- prove authentic location, speed, vehicle/driver identity, driving/rest and required authority event data end to end;
- retain API rights, support/RMA, data-flow, privacy and incident obligations.

**Current evidence status:** MISSING / provider selection required.

### CST role decision

CST provides a Tracking Services Registration path for entities providing tracking services. The current official service page publishes a SAR 10,000 fee and requires, among other application information, a valid commercial registration and server/data-storage location information.

OpsTrax must not assume it needs or already holds this registration. The legal/commercial architecture must first decide whether:

1. OpsTrax is only the TMS/fleet intelligence layer using an already registered/qualified Saudi provider; or
2. an OpsTrax/Kode Kinetics Saudi entity will itself provide the regulated tracking service.

The second model requires a separate Saudi eligibility/registration/qualification workstream.

### PDPL / privacy boundary

Before production use of GPS, driver documents, driver-facing video, safety events or AI scores, complete:

- controller/processor/subprocessor map;
- data inventory and purpose map;
- notice/lawful-basis/consent analysis as applicable;
- retention and deletion schedule;
- RBAC and audit reconstruction;
- data-subject request workflow;
- cross-border transfer assessment/safeguards;
- incident handling and provider DPA/SLA;
- human-review safeguards for consequential AI/safety decisions.

## 3. Required real-world certification evidence

Seeded/demo data, mock provider responses, static screenshots, backend-only tests and marketing claims cannot close a regulated gate.

Each active market must prove with authentic data:

**Device/vehicle -> regulated provider boundary -> provider cloud/app/authority boundary -> OpsTrax ingest -> canonical identity/provenance -> HOS/compliance engine -> driver -> dispatcher -> compliance administrator -> inspection/export/report.**

Required evidence domains:

| Domain | Canada | KSA |
|---|---|---|
| Exact regulatory source/version | Required | Required |
| Exact provider/device/app | Required | Required where activity requires tracking linkage |
| Current certification/qualification status | Required | Required where applicable |
| Commercial/API rights | Required | Required |
| Real hardware/account data | Required | Required |
| HOS boundary tests | Required | Required |
| Offline/reconnect/backfill | Required | Required |
| RBAC/tenant isolation | Required | Required |
| Privacy/data residency | Required | Required |
| Visible browser/client journeys | Required | Required |
| Independent regulatory review | Required | Required |
| Independent Security/SDET/Fleet Product acceptance | Required | Required |
| 0 open P0/P1 | Required | Required |

## 4. Permitted sales language before gates close

Allowed:

- "Canada compliance-ready architecture"
- "Designed to integrate with Transport Canada-certified ELD solutions"
- "Saudi TGA/WASL-ready integration architecture" where the demonstrated features genuinely support that statement
- "Designed to integrate with qualified local tracking providers"
- "Designed to support applicable HOS, privacy and audit workflows"

Not allowed until exact evidence exists:

- "Transport Canada-certified OpsTrax ELD"
- "OpsTrax is a certified ELD"
- "TGA-approved OpsTrax tracking service"
- "CST-registered OpsTrax tracking provider"
- blanket "fully compliant in Canada/KSA" claims

## 5. Acceptance governance

Implementation teams do not certify their own work. For each market the acceptance board includes:

- CTO / program owner;
- independent jurisdiction regulatory SME;
- Principal SDET;
- Security/Privacy;
- Fleet Product;
- Commercial/Legal.

P0 regulatory/security claims require at least two independent expert perspectives. Final decision is GO / LIMITED GO / NO-GO with exact SHA/environment, evidence references, customer limitations, permitted sales claims and re-review/expiration conditions.

## 6. Immediate execution links

- Canada lane: GitHub #165
- KSA lane: GitHub #166
- Saudi HOS P0 correction: GitHub #167
- Canada HOS expansion: GitHub #168
- Canada certified ELD procurement: GitHub #169
- KSA qualified tracking-provider procurement: GitHub #170
- Real-browser certification evidence: GitHub #171
- Hardware bench/vehicle evidence: GitHub #180

This document is an engineering/commercial compliance baseline, not legal advice or an external certification certificate.
