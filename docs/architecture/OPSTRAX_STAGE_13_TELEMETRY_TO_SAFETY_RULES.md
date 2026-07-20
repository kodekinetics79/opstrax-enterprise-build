# Opstrax Stage 13 Telemetry to Safety Rules

## Live Bridge Rules

| Telemetry Signal | Safety Outcome | Evidence | Notes |
| --- | --- | --- | --- |
| `stale_device` | Open safety alert and recommendation-only follow-up | `backend-dotnet/Services/TelemetryBackgroundService.cs` | AI can recommend review, not auto-close |
| Repeated speeding | Create or escalate safety event | `backend-dotnet/Services/SafetyBackgroundService.cs` | Deterministic, tenant-scoped, idempotent |
| Geofence breach | Create safety event and queue coaching or review | Safety service + telemetry alerts | No direct business mutation by AI |
| Low driver score | Recompute driver safety score and surface coaching need | `driver_safety_scores` and dashboard queries | Score is computed server-side |

## Safety Guardrails

- Safety records remain tenant-scoped.
- Workflow transitions stay server-authorized.
- The dashboard only summarizes persisted facts.
- The bridge must not fabricate driver behavior.

