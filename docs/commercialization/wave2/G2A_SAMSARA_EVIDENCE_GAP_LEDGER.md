# G2A Samsara evidence and gap ledger

**Owning gate:** G2A / GitHub Issue #115
**Activation:** `CR-2026-09-02-01`
**Capability truth:** **PILOT**
**Gate decision:** **HOLD — code readiness may progress; production certification may not close without authorized real-account evidence**

## Candidate control

- Branch: `wave2/g2a-samsara-readiness`

Alternative provider registrations are tracked in `G2_PROVIDER_ACCESS_REGISTRATION_LEDGER.md`. Samsara remains open as the current G2A provider candidate; registering Geotab, Motive, or Platform Science acquires options but does not certify or activate another connector workstream.
- Activation baseline: `1357687ee8d5e0a2be4f36e8cfd8f70770b2f42c`
- Latest protected implementation candidate: `1792c9b1882db51c7c43676aba0448bb4d6a346d` in PR #118; all 11 hosted controls passed in workflow run `33628368029`, including 550 PostgreSQL tests, production rehearsal and exact-SHA evidence packaging.
- The two-scope handshake and fail-closed pagination-envelope hardening described below is a replacement working tree, not an exact candidate until committed. Its commit must be rebound to its own hosted CI and exact-SHA evidence in Issue #115 before any deployment or field claim.
- Wave 2 work must not replace or broaden the frozen G1A controlled-pilot candidate `e2230425a8e14249d2c0f477a7ec7b713a6ab27e` without separate change control.

## Evidence status

| Acceptance area | Current evidence | Status | Missing closure evidence |
|---|---|---|---|
| Commercial truth | Catalog and legacy fixture correction start Samsara as `Disconnected` / `Never`; no seeded account or sync claim | READY FOR REVIEW | Exact-SHA migration/deploy proof and same-journey browser retest |
| Tenant authentication | Dedicated tenant-scoped `/sync`; generic caller-supplied sync action rejected | READY FOR REVIEW | Independent security acceptance and authorized-account adversarial test |
| Credential storage | Provider token aliases are encrypted/redacted; disconnect clears stored credential and cursor | READY FOR REVIEW | Real token lifecycle, provider-side revocation and reconnect evidence |
| Provider handshake | Replacement hardening requires successful read-only calls to `GET /fleet/vehicles?limit=1` and the bounded vehicle statistics feed before setting `Connected`; partial-scope and malformed-envelope responses fail closed | CODE READY / FIELD BLOCKED | New exact-SHA hosted proof plus authorized Samsara admin/account/token, provider responses and both-scope evidence |
| Discovery | Handshake proves provider access; an explicit bounded discovery sync then persists provider device identities and unmatched history without inferring ownership | CODE READY / FIELD BLOCKED | Customer-visible authorized-account discovery evidence |
| Mapping | Sync creates a provider device identity without inferring asset ownership; the UI links the explicit Map step to effective-dated device installation | READY FOR REVIEW | Authorized operator mapping acceptance with real provider devices |
| Event-time lineage | Current and delayed events retain device, installation, vehicle, assignment, trip, driver and branch lineage; ended-installation events remain history-only | READY FOR REVIEW | Independent exact-SHA test acceptance and real-provider delayed/backfill evidence |
| Reconciliation | Provider event identity is idempotent; duplicate pages do not advance live state or create duplicate alerts; valid pre-seven-day history is retained while invalid fixes are counted | READY FOR REVIEW | Real pagination/backfill/retry evidence with provider rate limits |
| Live projection | Only the current effective installation advances latest state, SSE or alerts | READY FOR REVIEW | Authorized provider journey through the live map and operational monitoring |
| Unmatched devices | Provider events are retained with null vehicle/installation lineage; no vehicle is guessed | READY FOR REVIEW | Customer mapping/remediation journey and real unmatched-device evidence |
| Manual sync | Sync requires a verified connection and reports positions written, vehicles seen, unmatched, historical-only and rejected counts | READY FOR REVIEW | Real-account sync and browser evidence |
| Worker recovery | Per-integration lease, durable last-attempt fairness rotation, four-tenant concurrency, five-page/60-second worker budget, repeated/empty/missing-cursor fail-closed guards, terminal persistence of only validated complete-page cursor progress, bounded 429/5xx retry and explicit single-attempt transport-failure truth exist | READY FOR REVIEW | New exact-SHA hosted proof plus rate-limit, outage, recovery, restart, backlog and soak evidence against a real provider path |
| Disconnect/reconnect | Generation-bound DB lease blocks stale tests/syncs; row-locked configuration merge prevents old credentials from being restored after disconnect; provider-side revocation warning remains explicit | READY FOR REVIEW | Real revoke/disconnect/reconnect journey and independent browser/Security acceptance |
| Customer UI | Samsara-specific token field; Save/Test performs a real handshake; explicit Discover → Map → Validate journey; separate handshake, polling-attempt and authentic provider-event freshness signals; one-minute refresh; transition-stable live announcements; labels and dialog focus containment/restoration are present | READY FOR REVIEW | Visible exact-SHA browser/accessibility evidence; qualified-human accessibility acceptance; customer-visible branch ownership remains an explicit limitation and needs staff-assisted tenant-scoped confirmation |
| Customer onboarding/support | Customer-executable account, environment, evidence, journey, failure/recovery, handoff and stop-condition runbook is prepared for versioning in `G2A_SAMSARA_CUSTOMER_CERTIFICATION_RUNBOOK.md` | PREPARED / FIELD BLOCKED | Published exact-SHA pack, authorized customer execution and Fleet Product/SRE acceptance |
| Performance/support | Bounded worker/manual-sync controls plus quantitative freshness/status and external-response floors and reproducible sandbox, bounded-pilot and large-fleet profiles are prepared; no representative provider-account result exists and no connector-alert acknowledgment product feature is claimed | PREPARED / FIELD BLOCKED | Published exact-SHA pack plus authentic fleet-sized sync/backfill/soak and recovery acceptance |

## Local preparation evidence

The following results are readiness evidence only and do not substitute for provider evidence:

- The protected-environment migration runner enrolls Stage 96 immediately after Stage 95. Exact candidate `1792c9b1882db51c7c43676aba0448bb4d6a346d` passed all 11 hosted controls in workflow run `33628368029`, including **550 PostgreSQL tests**, production rehearsal and exact-SHA evidence. The later two-scope handshake/pagination working tree still requires its own protected rerun.
- Deterministic PostgreSQL interleaving proves a handshake result and telemetry page captured before disconnect cannot restore connector state or write device/history/latest/alert rows.
- The opposite ordering is also executable evidence: once a provider-write transaction holds the integration row lock, disconnect waits, then invalidates the committed operation before returning.
- Row-locked configuration tests cover configure-first and disconnect-first ordering and prove cleared credentials cannot be resurrected.
- The database test also exposed and fixed a pre-conflict identity-trigger interaction: transaction-scoped advisory locking now prevents discovery from falsely quarantining an existing device.
- Concurrent first discovery now has executable PostgreSQL evidence: both contenders resolve one device identity and create no ambiguity quarantine.
- Worker fairness has executable PostgreSQL evidence: failed attempts advance a durable attempt clock so a repeatedly failing prefix cannot permanently exclude later tenants from the bounded candidate window.
- Samsara event-time transfer/backfill test proves delayed pre-transfer data stays on the ended installation and cannot overwrite the new vehicle's live state.
- Valid provider fixes older than seven days are retained in history; impossible/future/invalid fixes are explicitly counted as rejected.
- The prior `e916c28b4f21d905611c8a9035aca328ab8059e9` candidate's isolated exact-schema focused suite passed **24/24**, including the PostgreSQL interleavings above plus connector and launch-hardening contracts. It remains historical evidence only.
- Broader non-database .NET regression: **2,229/2,229 passed**; API/test build completed with zero errors (pre-existing warnings remain).
- Frontend lint, full contract suite, production build and bundle budget all passed.
- Three independent AI SDET perspectives identified and re-reviewed the Stage 96 enrollment, sync/handshake truth, provider-event freshness, bounded-time and pagination-integrity cursor progress, recovery-runbook and announcement defects. A later main-agent hardening review found that the handshake proved only `Read Vehicles`, not the separate `Read Vehicle Statistics` scope, and that a malformed 200 response could omit required pagination state. The replacement working tree fixes both and adds regression coverage. AI review is supporting assurance only, not qualified-human Appendix B sign-off.
- Exact candidate `1792c9b1882db51c7c43676aba0448bb4d6a346d` passed all 11 hosted controls in [workflow run `33628368029`](https://github.com/kodekinetics79/opstrax-enterprise-build/actions/runs/33628368029), including that run's exact-SHA evidence package. Those hosted results do not cover the later two-scope handshake/pagination working tree; no exact-SHA Wave 2 deployment or authenticated provider browser journey is claimed.
- The current connector is a bounded polling implementation fixed to the US API cloud (`https://api.samsara.com`). EU/UK and Canada regional API clouds and webhook delivery are not claimed by this candidate.

## Non-negotiable external dependencies

1. Authorized Samsara customer/admin or partner-sandbox account and tenant-approved API token. The program owner reported submitting the partner registration on 2026-09-01/02, but no provider confirmation email, Developer Portal/sandbox invitation, organization authorization or credential has been independently verified; no provider evidence is claimed before that proof exists.
2. Provider responses for connect, discovery, pagination, sync, backfill, rate-limit and recovery cases.
3. Customer-managed discover → map → validate → sync → monitor → disconnect → reconnect evidence.
4. Independent SDET and Security acceptance of the exact candidate.
5. Fleet Product acceptance of the customer workflow and operational limitations.
6. Exact-SHA deployment and same-journey browser retest in an isolated Wave 2 environment.

Until every applicable dependency is satisfied, Samsara remains **PILOT** and Issue #115 remains open.
