# G2A Samsara evidence and gap ledger

**Owning gate:** G2A / GitHub Issue #115
**Activation:** `CR-2026-09-02-01`
**Capability truth:** **PILOT**
**Gate decision:** **HOLD — code readiness may progress; production certification may not close without authorized real-account evidence**

## Candidate control

- Branch: `wave2/g2a-samsara-readiness`
- Activation baseline: `1357687ee8d5e0a2be4f36e8cfd8f70770b2f42c`
- This ledger does not name the working tree as an exact release candidate. The exact PR head SHA, CI result and any deployed SHA must be recorded in Issue #115 after the change is committed.
- Wave 2 work must not replace or broaden the frozen G1A controlled-pilot candidate `e2230425a8e14249d2c0f477a7ec7b713a6ab27e` without separate change control.

## Evidence status

| Acceptance area | Current evidence | Status | Missing closure evidence |
|---|---|---|---|
| Commercial truth | Catalog and legacy fixture correction start Samsara as `Disconnected` / `Never`; no seeded account or sync claim | READY FOR REVIEW | Exact-SHA migration/deploy proof and same-journey browser retest |
| Tenant authentication | Dedicated tenant-scoped `/sync`; generic caller-supplied sync action rejected | READY FOR REVIEW | Independent security acceptance and authorized-account adversarial test |
| Credential storage | Provider token aliases are encrypted/redacted; disconnect clears stored credential and cursor | READY FOR REVIEW | Real token lifecycle, provider-side revocation and reconnect evidence |
| Provider handshake | Real `GET /fleet/vehicles?limit=1`; only a successful provider response can set `Connected` | CODE READY / FIELD BLOCKED | Authorized Samsara admin/account/token, provider response and scope evidence |
| Discovery | Handshake proves provider access; an explicit bounded discovery sync then persists provider device identities and unmatched history without inferring ownership | CODE READY / FIELD BLOCKED | Customer-visible authorized-account discovery evidence |
| Mapping | Sync creates a provider device identity without inferring asset ownership; the UI links the explicit Map step to effective-dated device installation | READY FOR REVIEW | Authorized operator mapping acceptance with real provider devices |
| Event-time lineage | Current and delayed events retain device, installation, vehicle, assignment, trip, driver and branch lineage; ended-installation events remain history-only | READY FOR REVIEW | Independent exact-SHA test acceptance and real-provider delayed/backfill evidence |
| Reconciliation | Provider event identity is idempotent; duplicate pages do not advance live state or create duplicate alerts; valid pre-seven-day history is retained while invalid fixes are counted | READY FOR REVIEW | Real pagination/backfill/retry evidence with provider rate limits |
| Live projection | Only the current effective installation advances latest state, SSE or alerts | READY FOR REVIEW | Authorized provider journey through the live map and operational monitoring |
| Unmatched devices | Provider events are retained with null vehicle/installation lineage; no vehicle is guessed | READY FOR REVIEW | Customer mapping/remediation journey and real unmatched-device evidence |
| Manual sync | Sync requires a verified connection and reports positions written, vehicles seen, unmatched, historical-only and rejected counts | READY FOR REVIEW | Real-account sync and browser evidence |
| Worker recovery | Per-integration lease, durable last-attempt fairness rotation, four-tenant concurrency, five-page/60-second worker budget, repeated-cursor fail-closed guard and bounded 429 retry exist | READY FOR REVIEW | Rate-limit, outage, recovery, restart, backlog and soak evidence against a real provider path |
| Disconnect/reconnect | Generation-bound DB lease blocks stale tests/syncs; row-locked configuration merge prevents old credentials from being restored after disconnect; provider-side revocation warning remains explicit | READY FOR REVIEW | Real revoke/disconnect/reconnect journey and independent browser/Security acceptance |
| Customer UI | Samsara-specific token field; Save/Test performs a real handshake; explicit Discover → Map → Validate journey, labels and dialog focus containment/restoration are present | READY FOR REVIEW | Visible exact-SHA browser evidence and accessibility review |
| Performance/support | No representative provider-account scale or soak result exists | BLOCKED | Fleet-sized sync/backfill/soak, SLO, alerting, runbook and recovery acceptance |

## Local preparation evidence

The following results are readiness evidence only and do not substitute for provider evidence:

- Full production migration chain applied to an isolated PostgreSQL 17 database, including Stage 94 provider-truth correction and additive Stage 95 operation lease.
- Deterministic PostgreSQL interleaving proves a handshake result and telemetry page captured before disconnect cannot restore connector state or write device/history/latest/alert rows.
- The opposite ordering is also executable evidence: once a provider-write transaction holds the integration row lock, disconnect waits, then invalidates the committed operation before returning.
- Row-locked configuration tests cover configure-first and disconnect-first ordering and prove cleared credentials cannot be resurrected.
- The database test also exposed and fixed a pre-conflict identity-trigger interaction: transaction-scoped advisory locking now prevents discovery from falsely quarantining an existing device.
- Concurrent first discovery now has executable PostgreSQL evidence: both contenders resolve one device identity and create no ambiguity quarantine.
- Worker fairness has executable PostgreSQL evidence: failed attempts advance a durable attempt clock so a repeatedly failing prefix cannot permanently exclude later tenants from the bounded candidate window.
- Samsara event-time transfer/backfill test proves delayed pre-transfer data stays on the ended installation and cannot overwrite the new vehicle's live state.
- Valid provider fixes older than seven days are retained in history; impossible/future/invalid fixes are explicitly counted as rejected.
- Local exact-schema focused suite: **24/24 passed**, including the PostgreSQL interleavings above plus connector and launch-hardening contracts.
- Broader non-database .NET regression: **2,229/2,229 passed**; API/test build completed with zero errors (pre-existing warnings remain).
- Frontend lint, full contract suite, production build and bundle budget all passed.
- Three independent AI SDET perspectives found no remaining P0/P1 for code/UI, functional and resilience readiness. These reviews are supporting assurance only, not qualified-human Appendix B sign-off.
- Exact PR head SHA, hosted CI and any exact-SHA browser/deployment evidence will be recorded after publication; none is claimed by this working-tree ledger.

## Non-negotiable external dependencies

1. Authorized Samsara customer/admin account and tenant-approved API token.
2. Provider responses for connect, discovery, pagination, sync, backfill, rate-limit and recovery cases.
3. Customer-managed discover → map → validate → sync → monitor → disconnect → reconnect evidence.
4. Independent SDET and Security acceptance of the exact candidate.
5. Fleet Product acceptance of the customer workflow and operational limitations.
6. Exact-SHA deployment and same-journey browser retest in an isolated Wave 2 environment.

Until every applicable dependency is satisfied, Samsara remains **PILOT** and Issue #115 remains open.
