# Opstrax ELD 1.0 — Regulatory Gate Matrix

Status values: `GREEN`, `IN-PROGRESS`, `BLOCKED`, `NOT-STARTED`.

| ID | Jurisdiction | Requirement / Gate | Status | Evidence required to turn GREEN | Next action |
|---|---|---|---|---|---|
| GOV-001 | Common | Kode Kinetics LLC is the named provider/applicant | GREEN | Candidate manifest | Keep naming consistent |
| GOV-002 | Common | Controlled Opstrax ELD 1.0 certification branch exists | GREEN | `cert/opstrax-eld-1.0` | Protect certification-relevant changes |
| GOV-003 | Common | Exact backend SHA frozen | NOT-STARTED | Recorded SHA + passing CI | Select release candidate after first compliance sprint |
| GOV-004 | Common | Exact driver-app version frozen | NOT-STARTED | Signed build + version + SHA | Identify current driver-app implementation |
| GOV-005 | Common | Exact hardware/firmware frozen | NOT-STARTED | OEM/model/revision/firmware/BOM | Select only after protocol matrix is approved |
| GOV-006 | Common | Certification impact change-control process | IN-PROGRESS | PR template/check + owner + evidence path | Implement repository control artifacts |
| US-001 | USA | FMCSA ELD Provider Account for Kode Kinetics LLC | BLOCKED | Provider-account access | Supply exact legal/provider form data and submit official form |
| US-002 | USA | Provider portal ICD/Web Services resources obtained | BLOCKED | Controlled copies/hash/version notes | Requires US-001 |
| US-003 | USA | Provider public/private key pair integrated | BLOCKED | Key/cert integration test | Requires US-001/US-002 |
| US-004 | USA | Minimal 2–3 event output file generated | NOT-STARTED | Versioned sample output file | Implement generator spike |
| US-005 | USA | FMCSA File Validator passes minimal file | NOT-STARTED | Validator result evidence | Requires US-004 |
| US-006 | USA | Full output-file requirements implemented | NOT-STARTED | Complete validator samples | Expand incrementally after US-005 |
| US-007 | USA | Web eRODS rendering checked | NOT-STARTED | Screenshot/log evidence | Upload validated sample |
| US-008 | USA | Telematics transfer option implemented | NOT-STARTED | Web Services + Email test evidence | Implement after provider portal access |
| US-009 | USA | Driver authentication / unique accounts | IN-PROGRESS | Automated + browser/mobile tests | Map existing auth to ELD rules |
| US-010 | USA | Automatic driving-status event generation | NOT-STARTED | Vehicle/simulator evidence | Implement against certification test cases |
| US-011 | USA | Unidentified-driving workflow | NOT-STARTED | Assignment/rejection/audit tests | Build and test |
| US-012 | USA | Edits + annotations + original-record preservation | NOT-STARTED | Immutable audit evidence | Build compliance-specific event model |
| US-013 | USA | Personal conveyance / yard move behavior | NOT-STARTED | Test evidence | Build and test policy controls |
| US-014 | USA | Required malfunctions + data diagnostics | NOT-STARTED | Fault injection evidence | Build deterministic fault suite |
| US-015 | USA | Engine synchronization | NOT-STARTED | Physical ECM evidence | Select ECM interface then bench/road test |
| US-016 | USA | Location, odometer, engine-hour capture | NOT-STARTED | Physical/simulator traces | Validate with ECM/GNSS source |
| US-017 | USA | ELD integrity/audit sequencing | NOT-STARTED | Tamper/integrity tests | Implement compliance event chain |
| US-018 | USA | Full internal FMCSA test matrix passes | NOT-STARTED | Requirement-to-test traceability report | Execute before self-certification |
| US-019 | USA | Provider self-certification decision approved | NOT-STARTED | Signed internal compliance declaration | Only after US gates pass |
| US-020 | USA | Opstrax ELD registered on FMCSA list | NOT-STARTED | Official FMCSA registered-list entry | Final U.S. green gate |
| CA-001 | Canada | Accredited certification body selected | IN-PROGRESS | Executed intake/engagement | Send FPInnovations + COMDriver Tech intake from Kode Kinetics mailbox |
| CA-002 | Canada | Formal certification standard baseline confirmed | IN-PROGRESS | Written CB confirmation | Engineer v1.3 delta now; confirm formal campaign baseline |
| CA-003 | Canada | Canadian technical-standard delta matrix complete | NOT-STARTED | Requirements matrix | Map v1.3 against U.S. implementation |
| CA-004 | Canada | Exact hardware/software configuration accepted for testing | NOT-STARTED | CB intake acceptance | Requires GOV-003/004/005 |
| CA-005 | Canada | Simulation test campaign passes | NOT-STARTED | CB/internal reports | Build pre-cert harness first |
| CA-006 | Canada | Bench test campaign passes | NOT-STARTED | CB/internal reports | Requires hardware candidate |
| CA-007 | Canada | In-vehicle test campaign passes | NOT-STARTED | CB/internal reports | Requires physical truck/ECM |
| CA-008 | Canada | Certification-body findings closed | NOT-STARTED | Closure report | Remediate all findings |
| CA-009 | Canada | Certification number issued | NOT-STARTED | Certificate | External CB gate |
| CA-010 | Canada | Opstrax appears Active on Transport Canada list | NOT-STARTED | Official TC active-list entry | Final Canada green gate |

## Execution rule

Work the matrix top-down only where there is a dependency. Independent software gates may proceed in parallel. `GREEN` requires durable evidence linked to the exact candidate build/hardware configuration.

## Immediate sprint

1. Resolve US-001 provider-account data.
2. Inspect existing Opstrax HOS/ELD implementation and map current capability to US-009 through US-017.
3. Implement US-004 minimal output-file generator.
4. Establish validator evidence directory and test-record format.
5. Build CA-003 U.S.-to-Canada delta matrix using Technical Standard v1.3.
6. Do not purchase/freeze ECM hardware until GOV-005 requirements are explicit.
