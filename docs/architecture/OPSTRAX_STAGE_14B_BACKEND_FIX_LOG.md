# Stage 14B Backend Fix Log

| Module | Endpoint / Service | Issue | Fix | RBAC | Tenant Scope | Test | Remaining Gap |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Tenant Admin | `GET /api/admin/permissions` | Backend endpoint was missing, forcing the UI to use a seed list | Added a live permission catalog endpoint that returns canonical RBAC keys | `users:view` | Tenant-scoped admin context | Source regression + build | None for this slice |
| Safety / incidents | `GET /api/incidents/{id}/timeline`, `GET /api/incidents/{id}/recommendations` | Client was using static fake arrays instead of real history | Backend endpoints already existed; client was rewired to them | `safety:view` | Tenant-scoped | Source regression + build | None for this slice |
| Tenant Admin | `AdminPermissions` helper | Need a fail-closed catalog read instead of seed-backed fallback | Added permission enumeration from `RolePermissionDefaults` | `users:view` | Tenant-aware auth context | Source regression | Add an explicit API test later if useful |

