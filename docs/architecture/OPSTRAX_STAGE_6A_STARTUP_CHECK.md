# Stage 6A Startup Check

| Area | Finding | Evidence | Risk | Action |
|---|---|---|---|---|
| Working directory | Correct repo root | `pwd` returned `/Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx` | Low | Continue locally only |
| Branch state | Cleanly identified branch | `git branch --show-current` returned `opstrax-product-main` | Low | No branch switch needed |
| Repo state | Existing uncommitted work present | `git status --short` showed Stage 5/6 local files and docs | Medium | Preserve existing local changes |
| Backend startup path | Canonical API startup is `backend-dotnet/Program.cs` | `Program.cs` registers schema services, foundation services, and endpoint mappings | Low | Use this as runtime entrypoint |
| Hosted services | Dispatcher is opt-in only | `OutboxDispatcherBackgroundService` is registered only when `OutboxDispatcher__Enabled=true` and production is allowed explicitly | Low | Keep worker disabled by default in prod |
| Local DB | Disposable Postgres is available | `zayra_pg` on `127.0.0.1:5433` with `opstrax_local` | Low | Use only for local verification |
| Schema presence | Legacy business tables are not fully present in `opstrax_local` | Only `rate_cards` and `job_charges` existed before the bridge logic ran | Medium | Keep bridge code tolerant of missing legacy tables |
| Auth config | Authorization is centralized and fail-closed | `RequirePermission` uses `IAuthorizationDecisionService` and records decisions | Low | Preserve fail-closed behavior |
| Production safety | No production touch detected | No deploy/push/production migration activity was performed | Low | Keep the stage local |

