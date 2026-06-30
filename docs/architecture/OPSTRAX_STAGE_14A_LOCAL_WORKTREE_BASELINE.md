# Stage 14A Local Worktree Baseline

| Area | Finding | Evidence | Risk | Action Taken | Next Action |
| --- | --- | --- | --- | --- | --- |
| Repo state | Worktree is already dirty from prior staged work, including backend, frontend, docs, and mobile files. | `git status --short` and `git diff --stat` were captured before any Stage 14A edits. | Medium | No unrelated files were reverted. | Keep Stage 14A changes scoped to the main app and docs. |
| Main app naming | Visible shell and module registry already use `Dashboard` for the main command center surface. | `frontend/src/layouts/AppShell.tsx`, `frontend/src/modules/moduleConfig.ts`, `frontend/src/pages/CommandCenterPage.tsx`. | Low | No rename rewrite was needed. | Preserve `/command-center` route compatibility while keeping the visible label as `Dashboard`. |
| Demo masking | Two live frontend service paths still had legacy fallback residue. | `frontend/src/services/fuelApi.ts`, `frontend/src/services/safetyApi.ts`. | High | Plan to remove the fake fallback behavior. | Patch the live service clients and add a regression test. |
| Scope control | Stage 14A is main-app completion only. Mobile work exists in the repo but is not part of this stage. | User instruction and current repo layout. | Medium | Mobile is being left untouched. | Keep all edits in the main web/backend app. |

