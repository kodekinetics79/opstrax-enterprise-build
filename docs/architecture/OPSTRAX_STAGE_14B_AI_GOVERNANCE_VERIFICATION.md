# Stage 14B AI Governance Verification

| AI Surface | Source Entity | Recommendation Only? | Direct Mutation Possible? | Guardrail Evidence | Risk | Final Status |
| --- | --- | --- | --- | --- | --- | --- |
| Dispatch recommendations | dispatch / assignment models | Yes | No | AI surfaces are surfaced as suggestions; dispatch actions remain separate backend mutations | Medium | Verified |
| Proof validation recommendations | proof package / validation summaries | Yes | No | Proof pages stay on backend-controlled submit / validate actions | Medium | Verified |
| Telemetry recommendations | telemetry live state / alerts | Yes | No | Telemetry views summarize risk and actions rather than mutating telemetry data | Medium | Verified |
| Safety recommendations | incidents / coaching / safety events | Yes | No | Safety mutations are explicit backend calls, not AI side effects | Medium | Verified |
| Maintenance recommendations | maintenance / work orders | Yes | No | Maintenance actions remain explicit and RBAC-gated | Medium | Verified |
| Revenue leakage recommendations | finance / profitability read models | Yes | No | Revenue/finance reads are separate from issue/payment mutation routes | Medium | Verified |
| Platform commercial recommendations | platform / tenant commercial ops | Yes | No | Platform admin surfaces remain separate from tenant operations | Medium | Verified |

