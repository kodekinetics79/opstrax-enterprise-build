# Opstrax Stage 13 UI Implementation Notes

## What Changed

- Main command center remains user-facing as `Dashboard`.
- `frontend/src/layouts/AppShell.tsx` no longer falls back to the word `Cockpit`.
- `frontend/src/pages/CommandCenterPage.tsx` now shows a live bridge strip for:
  - Safety
  - Maintenance
  - Fleet health
- Seed-data masking was removed from the operational API clients used by these surfaces.

## UI Behavior

| Area | Behavior | Notes |
| --- | --- | --- |
| Loading | Shows live loading state | No fabricated rows |
| Error | Shows explicit backend connectivity message | Honest failure is preferred |
| Empty | Shows no-data state | Does not invent operational records |
| Navigation | Links to the real safety / maintenance / fleet-health pages | Keeps the experience operational |

## Design Principle

- The dashboard should feel like a working operations surface, not a mock.
- Stage 13 intentionally favors live signals over demo scaffolding.

