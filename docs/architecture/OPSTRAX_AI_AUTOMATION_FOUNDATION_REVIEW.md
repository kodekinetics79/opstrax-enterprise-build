# OpsTrax AI Automation Foundation Review

| Area | Current Support | Missing Pieces | Risk | Required Foundation | Priority | P0/P1/P2 |
|---|---|---|---|---|---|---|
| Domain events | Recommendation tables and operational data exist | No `domain_events` table or canonical event contracts | AI cannot reliably reason from durable facts | Add a domain event backbone and event taxonomy | P0 |
| Rules engine | Deterministic calculations exist in SQL and services | No dedicated rules engine layer | LLM may be used where rules should decide | Add rule-first evaluation before AI reasoning | P0 |
| Risk engine | Many entities have `risk_score` columns | No unified risk scoring model by entity/type | Signals are fragmented | Define typed risk signals and scoring outputs | P0 |
| Reasoning runs | `ai_recommendations` exists | No `ai_reasoning_runs`, `ai_run_inputs`, `ai_run_outputs`, `ai_run_errors` | No traceability for AI decisions | Create traceable reasoning run records | P0 |
| Action requests | UI/API can suggest actions | No `ai_action_requests` or approval workflow | AI could become unsafe if expanded casually | Add request/approval/execution boundaries | P0 |
| Approvals | Some export and cost approvals exist | No unified AI approval matrix | High-risk actions lack governance | Add approval policies and request logs | P0 |
| Execution services | Backend services can mutate business data | No AI-only service boundary map | Direct writes could creep in later | Keep AI execution through typed services only | P0 |
| Outcome tracking | Audit and recommendation tables exist | No `ai_action_outcomes` or learning loop | No measurable value capture | Add outcome and learning records | P1 |
| Memory/knowledge | None formalized | No tenant-scoped memory/knowledge model | Potential leakage if ad hoc | Add masked, tenant-scoped memory and docs | P1 |
| Guardrails | Session/RBAC exists | No AI guardrail policies or blocked actions tables | Unsafe recommendations may slip through | Add policy and sensitive-field controls | P0 |
| Cost/value model | Some operational metrics exist | No AI business value accounting | Hard to justify automation | Track time/fuel/cost/downtime/revenue impact | P1 |

