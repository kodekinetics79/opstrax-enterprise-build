# Stage 5D Dispatcher Startup Check

| Area | Finding | Evidence | Risk | Action |
| --- | --- | --- | --- | --- |
| Backend startup path | `Program.cs` registers the dispatcher services and only adds the hosted loop when config enables it. | `backend-dotnet/Program.cs` binds `OutboxDispatcherOptions` and conditionally registers `OutboxDispatcherBackgroundService`. | Low | Keep the conditional registration in place. |
| Default safety | Dispatcher is disabled by default and production remains off unless explicitly allowed. | `backend-dotnet/appsettings.json` sets `OutboxDispatcher__Enabled=false` and `AllowProduction=false`. | Low | Preserve the default-off posture. |
| Background services | Existing hosted services are already present; the dispatcher is one more controlled hosted service, not an uncontrolled loop. | `backend-dotnet/Services/*BackgroundService.cs` and `backend-dotnet/Services/OutboxDispatcherBackgroundService.cs`. | Medium | Keep the polling loop bounded by config and cancellation tokens. |
| Outbox schema | `outbox_messages` has claim, retry, lock, and dead-letter fields needed for safe runtime dispatch. | `database/migrations/2026_06_28_stage5d_p0b1a3_dispatcher.sql` and `backend-dotnet/Services/FoundationSchemaService.cs`. | Low | No schema rollback needed; keep additive-only discipline. |
| Inbox schema | `inbox_messages` has dedupe and claim fields needed for safe processing. | `database/migrations/2026_06_28_stage5d_p0b1a3_dispatcher.sql`. | Low | Keep tenant-scoped dedupe and `SKIP LOCKED` claiming. |
| Local DB availability | Local disposable PostgreSQL is available and was used for verification. | `opstrax_local` on `127.0.0.1:5433` in the local container. | Low | Use local DB only for verification. |
| Production guardrails | No production DB or deployment path was used. | Local build/test run only; no push/deploy. | High if violated | Do not enable production worker mode unless explicitly approved. |

