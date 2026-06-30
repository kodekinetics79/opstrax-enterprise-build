# Opstrax Stage 13 Mobile Safety and Maintenance Readiness

## Readiness Notes

- The current backend contract is already suitable for a future mobile shell.
- The live Dashboard and bridge cards are role-agnostic read surfaces.
- Mobile-specific work should reuse the same tenant-scoped APIs instead of creating shortcuts.

| Area | Current State | Stage 13 Decision |
| --- | --- | --- |
| Safety read views | Live API exists | Ready for future mobile shell |
| Maintenance read views | Live API exists | Ready for future mobile shell |
| Fleet health summary | Live API exists | Ready for future mobile shell |
| Idempotency / retries | Existing platform rules apply | Keep on backend |

