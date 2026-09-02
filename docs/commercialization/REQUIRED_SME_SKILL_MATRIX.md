# OpsTrax Required SME & Skill Matrix

**Governance status:** BINDING APPENDIX TO MASTER ACTION PLAN v1.1  
**Owner:** CTO Office  
**Applies to:** Commercialization parent #110 and every release/certification gate

## Execution-agent invocation rule
Every fresh execution-room prompt must include this concise clause:

> Apply the Required SME & Skill Matrix as binding governance. Activate the mandatory specialists for the active gate, require independent assurance for critical claims, prohibit implementation teams from self-certifying their own work, and keep CTO GO / LIMITED GO / NO-GO authority.

The full skill list does **not** need to be recopied into every prompt. This file and the Master Action Plan are the standing source of truth.

## Competency depth standard
- **Principal / SME:** has personally designed, deployed, operated, certified, audited, or commercially delivered the relevant capability in real-world production conditions.
- **Senior practitioner:** can independently design, implement, troubleshoot, and produce evidence without tutorial-level guidance.
- **Independent assurance reviewer:** is logically separate from the implementation decision and may issue a RED finding without schedule pressure.
- **Field-evidence requirement:** physical-device, provider, regulatory, vehicle, browser, and recovery claims are reviewed by people with direct experience in that evidence domain.

## Mandatory disciplines
| Discipline | Required depth | Release accountability |
|---|---|---|
| Fleet / TMS Product & Operations | Fleet operations, dispatch, linehaul/last-mile, maintenance, DVIR, safety, trailer/assets, cold chain, POD, customer visibility, route/exception management, fleet KPIs | Customer workflow truth and operational completeness |
| Telematics / IoT & Protocol Engineering | TCP/UDP sockets, binary framing, CRC, IMEI/session lifecycle, replay, GPS, LTE/SIM/APN, MQTT/HTTP telemetry, provider adapters, store-and-forward, firmware/provisioning | Device/provider ingestion and canonical telemetry integrity |
| Heavy-Duty Vehicle / J1939 / CAN | SAE J1939, CAN transport, PGN/SPN/FMI, DM1/DM2, ECU addressing, engine/aftertreatment/fuel/DEF, OBD-II/J1979, vehicle electrical interfaces | Heavy-duty diagnostic and engine-data certification |
| ELD / HOS Regulatory Engineering | FMCSA Part 395 and ELD specs; driving detection, edits/certification, unidentified driving, yard move/personal conveyance, U.S. clocks/cycles; Canadian HOS and ELD certification | Regulatory truth, partner acceptance, HOS and inspection/transfer release |
| Video Telematics / Computer Vision | Dual-facing cameras, video codecs, LTE/event upload, edge storage, clip retrieval/playback, distraction/phone/drowsiness/seatbelt/collision/following-distance detection, coaching | Camera/video and safety-event integrity |
| Enterprise / Distributed Systems | Event-driven architecture, idempotency, outbox/inbox, queues, retries/back-pressure, distributed concurrency, event ordering, eventual consistency, failure recovery | Cross-service correctness under retries/outages/concurrency |
| PostgreSQL / Data Architecture | RLS, planning/indexing, telemetry/time-series scale, transactions/locks/deadlocks, migrations, pooling, retention, lineage, backup/restore | Tenant isolation, durability, scale and migration safety |
| Cybersecurity / Product Security | OWASP ASVS/API, BOLA/IDOR, RBAC/ABAC, JWT/MFA/OIDC/SCIM, secrets/key rotation, HMAC/device trust, rate limits, SAST/DAST, threat modeling, spoof/replay resistance | Security and adversarial release gate |
| SDET / Performance / Adversarial QA | Playwright/Chrome, API/contract/integration, large-fleet data, load/stress/soak/chaos, network/offline, responsive/a11y, console/HAR evidence, reproducible defect ledgers | Independent customer-style exact-SHA certification |
| SRE / DevOps / Reliability | Linux, Docker, CI/CD, TLS/proxies/LB/CDN, OpenTelemetry, SLO/SLA, rollback/canary, capacity, incident response, DR, backup/restore, runbooks | Production readiness, observability, resilience and recovery |
| Enterprise UI/UX + GIS | Dense fleet interfaces, dispatch/control towers, large tables, map/geofence UX, responsive/accessibility, exception-first workflows, GIS/geospatial reasoning | Usability and map truth at fleet scale |
| Commercial / Sales / Customer Success | Fleet SaaS pricing, ARR/MRR, hardware economics, reseller/installer channels, procurement, onboarding/SLA/support/RMA, packaging and positioning | Sellability, implementation readiness and claim discipline |
| Specialist support pool | Automotive electrical; cellular/RF and FCC/ISED; privacy/labor law for driver cameras; data retention/eDiscovery; FinOps/cloud cost; applied AI/ML | Activated when the gate touches these risks |

## Mandatory SME matrix by release gate
| Gate | Mandatory active SMEs | Independent assurance / decision rule |
|---|---|---|
| G1A M1/M2 closeout | CTO; Fleet/TMS Product; Principal SDET; Security/RBAC; PostgreSQL/Data; SRE; Enterprise UI/UX; GIS/Map SME for telematics | Independent SDET + Security; Fleet Product accepts customer workflow truth |
| G1B GT06 physical certification | CTO; GT06/Protocol; Telematics/IoT; Hardware Certification; Cellular/RF; Automotive Electrical; GPS/GIS; SDET; Security; SRE/Observability | Protocol/hardware implementer cannot self-certify; SDET + Hardware + Security sign-off |
| G2A Samsara production connector | CTO; Provider Integration; Telematics; Data Mapping; Security; SDET; Fleet Product; SRE | Real provider-account evidence + SDET + Security acceptance |
| G2B Certified ELD partner | CTO; U.S. ELD/HOS Regulatory; Canadian ELD/HOS Regulatory where applicable; Integration; Security; Commercial/Legal; Fleet Product | Regulatory status independently verified; no claim ahead of certified partner/device boundary |
| G3A HOS | CTO; HOS Regulatory; Driver Operations; Fleet Product; Backend/Data; SDET; Security; UI/UX | Regulatory SME + SDET jointly control acceptance; inspection/transfer and audit reconstruction mandatory |
| G3B Dual-facing camera | CTO; Video Telematics; Camera Hardware; Privacy; Security; Storage/SRE; SDET; Driver Safety Product | Privacy + Security + SDET mandatory before inward-facing production use |
| G4A Video Safety | CTO; Computer Vision/AI; Driver Safety; Video Telematics; Product; UI/UX; Privacy; SDET; Data | Provider/model evidence does not equal product acceptance; human-review/coaching workflow must pass |
| G4B Geotab/Motive/OEM | CTO; Provider Integration; Telematics; Data; Security; SDET; SRE; Fleet Product | Each provider certified independently through canonical connector lifecycle |
| G5A DeviceOps 2.0 | CTO; Hardware Operations; IoT/Connectivity; Firmware; Support/RMA; Customer Success; Procurement/Commercial; SDET; Security | Lifecycle supportability and replacement/RMA evidence required, not just UI completion |
| G5B J1939 depth | CTO; J1939/CAN; Automotive Electrical; Telematics; Hardware Certification; SDET; Data | Real vehicle/gateway evidence required for each supported PGN/SPN claim |
| G5C PT40 | CTO; Pacific Track/Vendor Protocol; Hardware Certification; Cellular/RF; Telematics; SDET; Security; GPS/GIS | Real capture + vendor parser/spec + bench/vehicle evidence; no guessed decoder |
| G6 Scale / Commercial Release | CTO; Principal Performance; SRE; PostgreSQL; Security; Chaos/Recovery SDET; Product; Customer Success; Commercial/Sales | Independent full-stack certification; support/SLA/recovery and commercial packaging operational |

## Expert-team operating rules
1. **Right experts, not maximum headcount.** Activate the disciplines required by the active gate; add adjacent specialists only when evidence exposes a material risk.
2. **No self-certification.** Authors may submit evidence; independent assurance accepts critical security, hardware, regulatory, provider, performance and customer-workflow claims.
3. **Two-perspective rule for P0 domains.** At least two independent expert perspectives for ELD/HOS, tenant isolation, device protocol/hardware, video privacy and final production release.
4. **Evidence beats seniority.** Any SME may issue RED. CTO determines disposition; the finding is not suppressed to protect schedule.
5. **Field-first escalation.** When code and real-world behavior disagree, physical/provider/browser/regulatory evidence is authoritative.
6. **Commercial truth review.** Commercial/Sales participates before a capability status rises so proposal language, package eligibility and limitations remain aligned with the Capability Truth Matrix.
