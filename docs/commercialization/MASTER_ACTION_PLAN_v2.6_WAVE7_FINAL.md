# OpsTrax Master Commercialization & Certification Action Plan — v2.6 Final Wave 7 Amendment

**Status:** CONTROLLED MASTER AMENDMENT — ACTIVE ON MERGE  
**Effective date:** 2026-09-03  
**Change control:** `CR-2026-09-03-05`  
**Base master:** `docs/commercialization/MASTER_ACTION_PLAN.md` v2.1  
**Active concurrency model:** v2.5 / `CR-2026-09-03-04`  
**Entry baseline:** `main@6674f52f5fb8902af0cb777f2e0a893a14173b4b`  
**Parent tracker:** #110

## 1. Program-owner directive

Add **Wave 7 as the final governed product wave** and run it concurrently with Waves 1–6 under the v2.5 maximum-controlled-parallel model.

Wave 7 is the enterprise/platform completion layer. It consolidates the previously proposed post-release ambitions into one final wave so the roadmap has a finite end-state rather than an open-ended sequence of feature waves.

Activation does not promote any capability. Existing certification, provider, hardware, regulatory, exact-SHA, visible-Chrome, Appendix B, independent-assurance, 0-P0/P1 and commercial-truth requirements remain binding.

## 2. Final portfolio

Waves 1–6 remain active exactly as governed by v2.5. Wave 7 adds four parallel gates:

| Gate | Final-wave objective | Truth until exit |
|---|---|---|
| G7A Enterprise Control Plane & Globalization | Enterprise identity, SCIM, hierarchy, IP/network policy, regional/data-residency control and multi-region operating model | DEVELOPMENT |
| G7B Fleet Intelligence & Governed Automation | Predictive maintenance, ETA/risk/anomaly/fuel intelligence and governed fleet copilot/automation | DEVELOPMENT |
| G7C Developer Platform & Certified Ecosystem | Public API/SDK/webhooks/developer portal plus partner/device/installer ecosystem and compatibility catalog | DEVELOPMENT |
| G7D Migration, Vertical Packs & Enterprise Final Release | Competitive migration tooling, vertical solution packs, 10K–50K enterprise acceptance and final enterprise-platform release disposition | DEVELOPMENT / RELEASE READINESS |

## 3. G7A — Enterprise Control Plane & Globalization

Build on the existing SSO connection service, identifier-first SSO discovery, MFA/RBAC/RLS and platform control plane rather than replacing them.

Required scope:
1. Production-grade OIDC/SAML tenant SSO lifecycle: configure -> verify metadata -> enable -> discover -> authenticate -> rotate/revoke -> audit -> disable/recover.
2. SCIM 2.0 or equivalent enterprise user/group provisioning with external identity, group-to-role/branch mapping, deprovisioning, replay/idempotency, least privilege and audit.
3. Bulk user/fleet onboarding with deterministic validation, error ledger and resumable import.
4. Enterprise hierarchy: parent account -> subsidiaries/business units -> branches/sites -> fleets, with explicit inherited-vs-local policy boundaries.
5. Conditional access: approved domains, IP/CIDR policy, session/MFA posture and break-glass controls without locking out platform recovery.
6. Data-residency policy by tenant/package and documented regional deployment boundary.
7. Multi-region architecture for tenants that require it: regional data authority, failover/DR, routing, observability, encryption/key ownership and no silent cross-region replication outside policy.
8. Enterprise audit/export/access-review evidence and privacy/offboarding controls.

Acceptance:
- real enterprise IdP evidence for OIDC/SAML and real SCIM client/provider where claimed;
- 0 P0/P1 or tenant/identity leakage;
- exact-SHA Chrome admin + end-user journeys;
- deprovisioning removes access within the declared SLA;
- regional placement and restore/failover evidence match policy;
- Security/Identity + Privacy/Data Residency + SRE + Principal SDET + Enterprise Product acceptance.

## 4. G7B — Fleet Intelligence & Governed Automation

The current product has descriptive analytics and AI recommendation scaffolding; Wave 7 must not relabel those as predictive models.

Required scope:
1. Canonical feature-quality/provenance layer for model inputs; unknown/stale/missing telemetry stays explicit.
2. Predictive maintenance risk with measurable horizon, calibration and explainable contributing signals.
3. ETA/SLA risk prediction using route/trip/stop/traffic/provider data available to the tenant.
4. Driver/safety risk scoring using only authorized, documented signals; protected characteristics and unrelated personal data are excluded.
5. Fuel/idle/route inefficiency detection and operational anomaly detection.
6. Forecasting for maintenance capacity, device/connectivity issues and selected operational demand where sufficient history exists.
7. Governed OpsTrax copilot: retrieves tenant-authorized facts, cites source records, distinguishes fact/inference/recommendation, records recommendation identity/confidence and never fabricates live fleet state.
8. Human-approved automation for bounded actions with preview, policy, idempotency, rollback/recovery and audit; no unsafe autonomous vehicle/device command path.
9. Model registry/versioning, offline evaluation, drift/quality monitoring, kill switch and incident rollback.

Acceptance:
- benchmark/evaluation set and declared metrics appropriate to each model;
- model outputs are materially better than trivial/rule baselines before marketing as predictive;
- no fabricated confidence or model-active UI when no model is serving;
- exact-SHA persisted workflow and adverse/no-data cases;
- AI/ML + Data + Fleet Product + Safety/Privacy + Security + Principal SDET acceptance.

## 5. G7C — Developer Platform & Certified Ecosystem

Build on the existing API, tenant API-key/webhook, integration and DeviceOps foundations.

Required scope:
1. Versioned public API surface with tenant-scoped OAuth/API credentials, granular scopes, rotation/revocation, rate limits and audit.
2. Webhook subscriptions with signed delivery, idempotent event identity, replay/redelivery controls, dead-letter visibility and tenant isolation.
3. Developer portal: credentials, API explorer/docs, webhook logs/replay, usage/limits, changelog, sandbox guidance and support escalation.
4. First-party SDK/reference clients for priority customer languages after the API contract stabilizes.
5. App/integration catalog with explicit lifecycle status: available, pilot, certified/production supported where applicable, deprecated and retired.
6. Certified Compatibility Catalog shared with DeviceOps: exact hardware/provider/firmware boundary, capabilities, limitations and evidence reference.
7. Installer/service partner registry with approved geography, training/certification evidence, install quality, RMA/support responsibility and status.
8. Marketplace/commercial entitlements may list only capabilities whose owning gates permit the stated claim.
9. Partner security review, credential/data minimization and revocation/offboarding.

Acceptance:
- external sample integration can authenticate, subscribe, consume/replay a webhook and recover without engineering intervention;
- no cross-tenant events, secret disclosure or unsigned webhook acceptance;
- documented compatibility/partner claims match evidence;
- Security + Developer Experience/API + Telematics + Commercial + Principal SDET acceptance.

## 6. G7D — Migration, Vertical Packs & Enterprise Final Release

This is the final product-program closure gate, not a waiver over earlier gates.

Required scope:
1. Migration/import framework for customer-owned exports and supported provider APIs from incumbent fleet systems, with mapping preview, reconciliation, duplicate handling, rollback and evidence ledger.
2. Competitive migration playbooks for priority sources (for example Samsara/Geotab/Motive) only where customer data/API rights permit.
3. Vertical packs that configure—not fork—the product: trucking/linehaul, last-mile/service fleet, cold chain, construction/heavy equipment and public/government fleet as commercially justified.
4. Each vertical pack defines roles, KPIs, workflow defaults, compliance boundaries, device/provider needs, onboarding and truthful limitations.
5. 10K / 25K / 50K enterprise fleet acceptance using realistic tenant hierarchy, devices, users, telemetry, jobs, alerts, reports and integrations as the architecture permits.
6. Executive control-plane UX, high-volume accessibility/responsiveness and bulk operations at enterprise scale.
7. Final package catalog, SLA/support model, security/privacy/legal pack, customer migration/runbook, deployment/DR evidence and Capability Truth reconciliation.
8. Competitive benchmark is evidence-driven: parity/differentiation claims cite working OpsTrax capability rather than roadmap text.

Final acceptance:
- all Wave 7 internal P0/P1 closed;
- 10K–50K acceptance thresholds pass for the final enterprise package or the documented supported tier is explicitly lower;
- migration evidence reconciles source -> imported -> rejected/needs-review counts with no silent loss;
- sold package contains only owning-gate accepted or explicitly permissible bounded capabilities;
- qualified Enterprise Product + SRE/Performance + Security/Privacy + Data + Customer Success + Commercial + Principal SDET acceptance.

## 7. Final release rule

Wave 7 may complete engineering while a specific earlier provider/device/regulatory gate remains on external evidence hold. It may not absorb or waive that dependency.

The program is considered fully complete only when the selected market package receives an evidence-backed final disposition:

# **OPSTRAX ENTERPRISE PLATFORM — COMMERCIAL RELEASE GO**

The final release record must list, per capability, its actual status: CERTIFIED, PRODUCTION READY, PILOT, DEVELOPMENT, ROADMAP or NOT OFFERED.

## 8. Concurrency

Wave 7 uses the v2.5 conflict-domain model:
- all four G7 lanes may run concurrently;
- shared migration/auth/design-system authorities remain serialized;
- frozen certification candidates remain immutable;
- external evidence never becomes a simulated pass;
- defect loop remains Observe -> Evidence -> Root Cause -> Fix -> Test -> Exact-SHA Deploy -> Same Journey Retest -> Close.

There is no Wave 8 in this governed product program. New post-release ideas enter normal product backlog/change management after the Wave 7 enterprise release disposition.