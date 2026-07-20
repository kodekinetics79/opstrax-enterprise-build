# OpsTrax — Project Identity & Boundaries

**This repo is OpsTrax** (fleet / dispatch / logistics SaaS).
Remote: `github.com/kodekinetics79/opstrax-enterprise-build`.

## ⛔ Repo boundary — do not cross
Only edit files **inside this repository**. Never modify sibling projects under
`~/Downloads` — especially **`zayra-ai-workforce`** (a separate product:
`Zayra.Api`, Next.js App Router, `/api/logistics/*`). The two share no code; the
Dispatch/Fleet naming overlap is coincidental, not shared code. A `PreToolUse`
hook (`.claude/hooks/guard-repo-boundary.py`) blocks out-of-repo Write/Edit, but
treat this rule as primary regardless. Do not paste another project's file paths
into this session and act on them.

## Architecture (so you don't guess)
- **Frontend**: Vite + React SPA (React Router, `frontend/`), deployed on Vercel.
  Pages in `frontend/src/pages`, shared UI in `frontend/src/components/ui.tsx`,
  API clients in `frontend/src/services`. Dispatch board lives in
  `DispatchCommandPage.tsx` (route `/dispatch`).
- **Primary backend**: `.NET` `Opstrax.Api` (`backend-dotnet/`), the real API.
  Local `:8088`, deployed on Render (`opstrax-enterprise-build-*.onrender.com`).
  Most endpoints are mapped in `backend-dotnet/Controllers/EndpointMappings.cs`.
- **Side service**: Node backend (`backend/`, `:8090`) — auth/integrations/telemetry only.
- **DB**: Neon Postgres (connection in `.env` / `backend-dotnet/appsettings.json`).
- **Multi-tenant**: 4 real companies; reads must be scoped by `company_id`.

## Conventions
- Frontend services use `withFallback` to seed data — a page rendering seed
  content usually means the live API call failed; fix the API, don't lean on the seed.
- Dispatch assignment status is the canonical lowercase P4 token vocabulary
  (`assigned`, `accepted`, `en_route_pickup`, `arrived_pickup`, `loaded`,
  `in_transit`, `arrived_delivery`, `delivered`, `exception`, `cancelled`).
  Legacy/title-case DB values are normalized via `NormalizeAssignmentStatus`.
