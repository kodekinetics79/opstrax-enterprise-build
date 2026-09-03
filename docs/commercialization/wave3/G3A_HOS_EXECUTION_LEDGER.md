# G3A — HOS Operational Workflow Execution Ledger

Parent: #110  
Gate: #128  
Activation: `CR-2026-09-03-01`  
Entry baseline: `main@aba2636c543c6f77cb47597383d4c2c8c32e61c8`  
Commercial truth at start: HOS structures **DEVELOPMENT**; Certified ELD/HOS **ROADMAP**.

## First observed gate defect — W3-A-TRUTH-001

**Severity:** P1 customer/compliance truth blocker  
**Status:** OBSERVED / ROOT CAUSE IDENTIFIED / FIX NOT YET CLOSED

### Observe

The current HOS surface can render remaining-drive/shift/cycle clock values and Warning/Violation states even though G2B has not yet supplied an authorized certified ELD source. The UI disclaimer correctly says OpsTrax is not a certified ELD, but the numerical clock bars can still look operationally authoritative.

### Evidence

- `backend-dotnet/Services/Batch6SchemaService.cs` creates `hos_clocks` with non-null defaults of 660 drive minutes, 840 shift minutes and 4200 cycle minutes, plus default status `OK`.
- `backend-dotnet/Services/DemoTenantSeeder.cs` writes explicit demonstration HOS clock values/statuses.
- `backend-dotnet/Controllers/DvirHosEndpoints.cs::HosDriversPilot` reads `hos_clocks` directly and returns those values without a certified-source/provenance gate.
- `HosSummaryPilot` labels the source only as `persisted_hos_logs_and_clocks`, which proves persistence but not regulatory authority.
- `frontend/src/pages/HosEldPage.tsx` renders the returned values as Drive Remaining / Shift Remaining / Cycle Remaining bars and Warning/Violation states.
- Existing product audit evidence previously identified that no service derives these remaining-time values from an authoritative ELD motion/duty-status source.

### Root cause

The pre-commercial HOS schema treated a convenience/demo clock snapshot as if it were an operational clock record. Persistence and UI wiring matured before source authority/provenance was made a first-class release boundary.

### Required focused remediation

1. Introduce explicit clock source/provenance/authority semantics and a source-observed timestamp.
2. Remove unsafe non-null legal-time defaults for new records; unknown must remain unknown.
3. Existing demo/legacy-unverified clocks must not be presented as legally actionable HOS remaining time.
4. Driver/fleet APIs must expose whether a clock is `Authoritative`, `ProviderPending`, `LegacyUnverified`, or equivalent controlled state.
5. UI must show `Unavailable / source not certified` instead of green/warning/violation clock bars when authority is not proven.
6. Any future automatic-driving source must be bound to the selected G2B provider/device/application boundary and preserve provider event identity.
7. Dispatch remaining-hours warnings must consume only an accepted authoritative clock source; they must fail closed when source authority/freshness is insufficient.
8. Add PostgreSQL + API + frontend contract tests proving legacy/demo records cannot create an actionable legal-hours claim.
9. Run exact-SHA visible Chrome same-journey retest after deployment.

### Closure rule

W3-A-TRUTH-001 closes only when no unverified/demo/default HOS clock can be mistaken for an authoritative legal-hours calculation in API, UI, dispatch warnings, exports or alerts. This fix does not by itself certify the HOS engine.

## Next bounded work

After W3-A-TRUTH-001 is closed, proceed in this order inside #128:

1. canonical duty-status source/audit envelope;
2. unidentified-driving queue;
3. driver edit/annotation/certification workflow;
4. jurisdiction/configuration-versioned clock engine;
5. malfunction/diagnostic response workflow;
6. inspection/transfer interfaces;
7. dispatch remaining-hours integration;
8. real certified-source exact-SHA acceptance when G2B evidence is available.

Apply Appendix B; implementation authors may not certify the result. Two independent qualified regulatory perspectives remain mandatory for P0 jurisdictional release claims.
