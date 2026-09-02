# Capability Inventory — RETEST-20260821-1035-R1

Recorded at bootstrap, before any file change. Discovered, not assumed.

| Required capability | Resolved to | Status |
|---|---|---|
| PostgreSQL / Neon | `psql` 18.4 client + Docker daemon UP (local isolated DB is provisionable). Ports 5432 and 5433 are already occupied — an isolated instance must use a free port. | AVAILABLE (native) |
| Neon branch/API automation | No Neon MCP server and no project `.mcp.json`. Disposable-branch work must be native (`psql` + Docker) or explicitly authorized against Neon. | **MISSING CAPABILITY** |
| .NET | `dotnet` 10.0.300 CLI; suites `backend-dotnet.Tests`, `Opstrax.Telematics.{Protocols,Security,Integration}Tests` | AVAILABLE |
| React / frontend | Vite + React SPA (React Router) — **not** Next.js App Router; `vercel:react-best-practices` skill available | AVAILABLE |
| Browser / Playwright / Chrome | Playwright 1.62.1 installed under `tests/e2e`; Google Chrome present | AVAILABLE |
| Security / RBAC | `security-review` skill + native source review | AVAILABLE |
| Telematics / IoT security | project skill `opstrax-telematics` | AVAILABLE |
| GitHub / PR | `gh` 2.93.0 | AVAILABLE |
| Vercel deployment/observability | `vercel` CLI 55.0.0 (outdated; 59.4.0 current). Vercel MCP plugin requires OAuth and is **unauthorized in this non-interactive session**. | DEGRADED |
| Render / API observability | No Render MCP; native HTTPS health/readiness probes + `render.yaml` | AVAILABLE (native) |
| Documentation | Native markdown under `docs/uat/RETEST-20260821-1035-R1/` | AVAILABLE |
| Spreadsheet / reporting | Native CSV ledger | AVAILABLE |

## Project skills discovered

`opstrax-telematics`, `production-diagnosis`, `demo-readiness`, `honest-interfaces` (in `.claude/skills/`).
No `.claude/agents/` and no `.agents/skills/` directory exists — specialist agents are constructed per work packet by the orchestrator.

## Guard in force

`.claude/hooks/guard-repo-boundary.py` (PreToolUse on Write/Edit) blocks out-of-repo edits. Sibling project `zayra-ai-workforce` is out of bounds.

## Stale directories excluded from all analysis

`.sso-wt/`, `.claude/worktrees/`, `node_modules/`, `tmp/qa/` — snapshot copies, not source of truth.
