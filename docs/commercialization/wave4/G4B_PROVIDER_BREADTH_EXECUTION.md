# G4B — Geotab / Motive / OEM Provider Breadth Execution Charter

**Gate:** #123  
**Change control:** `CR-2026-09-03-02`  
**Entry baseline:** `main@547f482dbf47e6f442c5d1f3e3b23806a49872cf`  
**Branch:** `wave4/g4b-provider-breadth`  
**Commercial truth:** ROADMAP

## First bounded implementation batch

Start with the shared connector lifecycle and provider decision record. Do not create a second telemetry truth model.

1. Inventory the existing canonical telemetry and Samsara connector contracts that every new provider must reuse.
2. Define a provider capability matrix for Motive, Geotab and OEM/direct candidates: auth, vehicles/devices/drivers, positions, engine state, odometer, diagnostics where actually available, HOS/safety boundaries, webhooks/polling, pagination/cursors, backfill, rate limits, commercial rights and geography.
3. Score Motive and Geotab first using real sales relevance, installed base, API depth, partner rights, implementation effort, support/RMA burden and target geography.
4. Shared adapter lifecycle: Connect -> Authenticate -> Discover -> Map -> Validate -> Backfill -> Incremental Sync/Webhook -> Monitor -> Recover -> Disconnect/Reconnect.
5. Deterministic mapping/reconciliation contract with explicit unmatched and ambiguous states.
6. Provider provenance/freshness/quality semantics and null-safe optional measurements; no missing-to-zero coercion.
7. Cursor/checkpoint, retry/backoff, rate-limit, replay/idempotency and stale-session fencing contracts.
8. Token/secret lifecycle, tenant/branch ownership and audit requirements.
9. Customer onboarding, mapping, reconciliation and sync-health UX contract with honest unsupported/no-data states.
10. Provider contract/replay tests may use synthetic fixtures only as supporting evidence and must be labelled as such.

## Provider decision checkpoint

A provider becomes the first implementation target only after the gate records:
- current official API capability evidence;
- an achievable authorized account/partner access path;
- acceptable commercial/integration rights for intended use;
- geography fit for initial customers;
- supportability and rate-limit/backfill feasibility;
- no requirement to falsify unsupported telemetry fields.

## Targeted evidence matrix

| Area | Supporting automation | Required final evidence |
|---|---|---|
| Adapter lifecycle | contract + integration tests | real provider customer journey |
| Mapping/reconciliation | deterministic tests | persisted provider-backed mapping |
| Provenance/freshness | DB + contract tests | authentic provider timestamps/data |
| Retry/recovery | replay/failure tests | network/rate-limit/restart recovery |
| Security/isolation | secret/RBAC/RLS tests | independent Security negative evidence |
| Scale | load harness | real/provider-bounded representative fleet |
| UI/UX | frontend contracts | visible Chrome desktop/tablet/mobile |

## First acceptance checkpoint

The first batch may merge only when:
- mandatory CI/release gates are green;
- 0 open P0/P1 in changed scope;
- no duplicate provider-specific truth schema is introduced;
- unsupported values remain explicit/unknown;
- tenant/branch isolation and stale-session fencing are retained;
- SDET independently replays the bounded lifecycle contract;
- no Motive/Geotab/OEM production-certified claim is made without real provider evidence.
