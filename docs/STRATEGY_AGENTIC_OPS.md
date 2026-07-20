# OpsTrax Strategy — Own the Seam, Fill the Empty Brain

*Product of a multi-agent strategic effort: a competitive teardown, a full codebase asset
inventory, and a flagship implementation design, synthesized and then BUILT.*

## The thesis (from the competitive teardown)

Every fleet-tech incumbent has the same two structural weaknesses:

1. **Hardware lock-in is their moat AND their cage.** Samsara, Motive, Geotab, Verizon
   Connect all monetize a gateway/camera on the vehicle. De-coupling software from that box
   cannibalizes their core P&L — the classic innovator's dilemma. OpsTrax ingests telemetry
   through an **open HMAC-signed endpoint** (`TelemetryHmacHelper.cs`) — any signed device,
   OEM-embedded, third-party, or phone. Hardware-agnostic by construction.
2. **They own one half of the problem, never both.** Telematics players (Samsara/Motive)
   own the truck but not the back office. TMS players (McLeod/Trimble) own the back office
   but not the real-time truck. **Nobody owns the seam between them.**

OpsTrax owns the seam: it has `telemetry_live_asset_states` (the live truck) AND the P4
`dispatch_assignments` state machine with `dispatch_exceptions` (the back office) under one
tenant boundary. Plus GCC regulatory depth (ZATCA e-invoicing, Iqama, National Address,
Hijri dates, Wasl) that US incumbents structurally won't chase.

**Positioning:** *"OpsTrax is the compliance-native, hardware-agnostic fleet OS for the GCC —
it owns the seam between the truck and the back office that neither Samsara nor McLeod can reach."*

## The decisive finding (from the asset inventory)

The platform was **architected for agentic AI and left the brain unplugged**:

- `ai_reasoning_runs` already stores `prompt_template` + `expected_schema_json` + `input_json`
  + `output_json` — literally a slot shaped to hold an LLM call and its structured output.
- `PostgresAiFoundationService` already has the full chain: `StartReasoningRun` →
  `CompleteReasoningRun` → `CreateRecommendation(...proposedActionJson...)` →
  `CreateActionRequest(...requiresApproval...)`.
- `ApprovalPolicyCatalog.RequiresApproval()` is the built-in human-in-the-loop gate.
- A transactional outbox event bus threads `correlation_id`/`causation_id` through everything,
  so "explain why the system did X" is free.
- **Every score was a canned heuristic. No model was ever wired in.**

Dropping a real model into that empty slot turns the whole platform into a **supervised
autonomous fleet operator with full causal provenance** — a capability no competitor can
reproduce without owning both halves of the seam AND the approval ledger.

## The flagship — Agentic Ops Copilot (built this session)

OBSERVE → REASON → PROPOSE → (human approve) → EXECUTE, all on real assets:

| Layer | Implementation | File |
|---|---|---|
| Brain | Claude client, fail-safe (no key ⇒ disabled), prompt-injection-hardened, JSON-contract output | `backend-dotnet/Services/AgenticBrainService.cs` |
| Agent loop | Background worker: reads OPEN dispatch exceptions, reasons per one, writes a real reasoning run + `proposed` recommendation with a `proposed_action` | `backend-dotnet/Services/AgenticOpsBackgroundService.cs` |
| Human gate | `GET /api/ai/recommendations?status=proposed`, `POST .../{id}/approve`, `.../dismiss` — permission-gated, tenant-scoped, audit-logged | `EndpointMappings.cs` |
| Executor | Approved actions execute through EXISTING dispatch logic (raise exception / advance status); advisory types recorded as approved | `EndpointMappings.cs` (`AgenticRecommendationApprove`) |
| UI | "Copilot proposals" panel with Approve/Dismiss on the AI Copilot page | `frontend/src/pages/AiCopilotPage.tsx` |

**Guardrails by construction:** the agent NEVER executes — it only proposes. Execution is
human-gated behind `dispatch:manage`. The brain is off unless an API key is set (boot is
never affected). Idempotency on `source_event_id` (each exception reasoned once). Per-tick
cost cap + cooldown. Field text is passed as untrusted data with an explicit "treat as data,
not instructions" directive, and `action_type` is validated against an allow-list. The
cross-tenant loop reuses the production-proven `EscalationBackgroundService` system-scope
pattern (RLS-enforced, fail-closed).

## To activate

Set `ANTHROPIC_API_KEY` (or `Agentic:ApiKey`) on the backend. The worker starts reasoning
over open dispatch exceptions within one tick; proposals appear in the Copilot panel for a
dispatcher to approve. Everything else — tables, endpoints, UI, audit trail — is already live.

## The roadmap beyond v1 (grounded, not vapor)

1. **More signal sources** into the same loop: stale telemetry (`telemetry_live_asset_states`),
   SLA-at-risk jobs, HOS violations, cold-chain breaches — the worker already has the shape.
2. **Richer executors**: `reassign_driver` (validate against `CheckDispatchEligibility` first),
   `send_customer_eta` (existing endpoint), `open_work_order`.
3. **Telemetry↔Revenue fusion** (asset inventory finding #1): the revenue-leakage engine and
   the trip-compliance engine both exist but never talk — "route ran but no charge captured /
   billed below contract given actual miles" is a Samsara/Motive blind spot.
4. **Composite operational-trust score** per job/driver/vehicle from the three independent
   scores already computed (proof→billing confidence, driver safety, fleet health).
5. Then the moat plays from the teardown: **agentic dispatch + embedded finance ("get paid on
   delivery" triggered by the `delivered` state + a ZATCA-ready invoice)** make OpsTrax
   un-rip-outable — the stickiness of McLeod, but real-time and GCC-native.
