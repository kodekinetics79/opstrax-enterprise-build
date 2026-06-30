# Stage 9 Startup Check

| Area | Finding | Evidence | Risk | Action |
|---|---|---|---|---|
| Runtime registration | Stage 9 schema and operational services are registered at startup. | `backend-dotnet/Program.cs` | Low | Keep the schema step gated and additive. |
| Production safety | Stage 9 schema runs only when explicitly enabled and is off by default in production. | `Stage9Schema:Enabled` guard in `Program.cs` | Low | Preserve the production default of disabled. |
| Permission gating | New POD and mobile operational routes use the centralized permission gate. | `backend-dotnet/Controllers/Stage9Endpoints.cs`, `backend-dotnet/Controllers/EndpointMappings.cs` | Medium | Keep all new routes fail-closed. |
| Tenant scope | Stage 9 service methods accept company/tenant scope and persist `company_id` on new tables. | `backend-dotnet/Services/Stage9OperationalFoundationService.cs`, `backend-dotnet/Services/Stage9SchemaService.cs` | Medium | Do not introduce cross-tenant reads or writes. |
| Approval safety | High-risk acceptance and waiver paths create approval requests instead of auto-completing the business effect. | `AcceptSmartAssignmentAsync`, `UpdateAccessDocumentStatusAsync` | Medium | Preserve approval-required behavior. |
| AI safety | Missing evidence paths create recommendations only; they do not execute business actions. | `SubmitProofPackageAsync`, `CreateSiteAccessRequirementAsync` | Medium | Keep AI advisory only in this slice. |
| Dispatcher compatibility | Stage 9 emits durable events through the existing foundation event path. | `PostgresDomainEventPublisher` usage in Stage 9 service | Low | Reuse the established outbox/dispatcher path. |

