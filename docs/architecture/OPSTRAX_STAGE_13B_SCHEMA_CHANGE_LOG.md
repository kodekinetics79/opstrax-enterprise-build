# Opstrax Stage 13B Schema Change Log

## Additions

| Table | Purpose | Notes |
| --- | --- | --- |
| `fleet_health_snapshots` | Durable company-level fleet-health projection | Stores score, risk level, reasons, and next action |

## Safety

- Additive only.
- No destructive statements.
- No production migration was run.
- Existing safety and maintenance tables were not renamed or dropped.

