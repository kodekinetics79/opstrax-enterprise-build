# OpsTrax Stage 4 P0-B0 Foundation Report

## Repo Readiness Snapshot

| Area | Finding | Evidence | Risk | Action Taken | Next Action |
|---|---|---|---|---|---|
| Working tree | Docs-only changes in progress | `git status --short -- .` | Low | Kept source untouched | Finish foundation docs |
| Backend root | Active .NET backend is `backend-dotnet/` | `backend-dotnet/Program.cs`, `backend-dotnet/Opstrax.Api.csproj` | Medium | Confirmed | Use this as migration source of truth |
| Frontend root | Active frontend is `frontend/` | `frontend/package.json` | Low | Confirmed | Keep build verification on this path |
| Node backend | Auxiliary Node service exists in `backend/` | `backend/package.json`, `backend/src/*` | Medium | Confirmed | Treat as supporting service, not authority |
| ORM/migration tool | No formal migration tool; schema bootstrap services run on startup | `backend-dotnet/Services/*SchemaService.cs`, `backend-dotnet/Program.cs` | High | Documented | Define local migration foundation and rollback strategy |
| DB provider | PostgreSQL in active runtime | `.env.example`, `backend-dotnet/Data/Database.cs`, `backend/src/lib/db.ts` | Medium | Confirmed | Align legacy docs/config references |
| Auth/session | Bearer session + CSRF + tenant header model | `backend-dotnet/Program.cs`, `backend/src/middleware/sessionAuth.ts`, `frontend/src/services/apiClient.ts` | Medium | Confirmed | Evaluate whether this is enough for policy engine |
| RBAC/permission model | Route-level permission checks and role defaults exist | `backend-dotnet/Controllers/EndpointMappings.cs`, `backend/src/middleware/sessionAuth.ts` | High | Confirmed | Document as insufficient for a centralized authorization engine |
| AI foundation | Recommendation endpoints and AI tables exist, but no event/action/approval engine | `backend-dotnet/Services/Batch1SchemaService.cs`, `backend-dotnet/Services/Batch2SchemaService.cs`, `backend-dotnet/Services/Batch3SchemaService.cs` | High | Confirmed | Document the missing event/risk/action foundation |
| IoT foundation | Device ingest, telemetry alerts, nonce replay protection exist | `backend-dotnet/Services/TelemetrySchemaService.cs`, `backend-dotnet/Controllers/EndpointMappings.cs`, `backend/src/modules/telemetry/telemetry.routes.ts` | Medium | Confirmed | Document what is durable versus demo-only |
| Event reliability | No formal outbox/inbox tables found | repo-wide search | High | Confirmed missing | Add foundation docs before schema expansion |

## Review Summary
- PostgreSQL is the active target, but migration/versioning is not formalized.
- AI is present as recommendation support, not as a governed automation engine.
- RBAC is real but still route-guard oriented, not a centralized policy engine.
- IoT ingestion exists, but durable event-driven automation is incomplete.
- Data classification, idempotency, and correlation standards are not yet formalized.

