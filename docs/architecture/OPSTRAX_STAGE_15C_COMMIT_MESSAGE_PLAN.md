# Stage 15C Commit Message Plan

| Commit Group | Recommended Message | Files Included | Risk | Notes |
|---|---|---|---|---|
| Backend foundations | `feat: harden backend foundation and live endpoints` | Backend source and schema/service changes | Medium | Keep endpoint and service changes together. |
| Frontend route/nav/UI | `feat: preserve dashboard and trips navigation` | App shell, dashboard, Trips, platform pages | Medium | Keep visible route changes together. |
| Telemetry / live map / safety / maintenance / finance | `fix: harden live operational surfaces` | Live map, telemetry, safety, maintenance, finance services and pages | Medium | Keep live-data honesty changes together. |
| Regression tests | `test: lock in stage regressions` | Backend regression tests | Low | Stage tests after code is reviewed. |
| Migrations | `db: add additive foundation migrations` | Reviewed migration SQL files | Low | Only additive migrations should be staged. |
| Architecture docs | `docs: add stage 15b verification set` | Stage 15B/15C review docs | Low | Keep evidence separate from runtime code. |
| Git hygiene | `chore: tighten local ignore rules` | `.gitignore` | Low | Safe local hygiene only. |

