# Stage 14B RBAC / Tenant Isolation Verification

| Area | Permission / Role | Backend Enforcement | Frontend Visibility | Tenant Scope | Verified By | Risk | Final Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Tenant app routes | Tenant roles, `dashboard:view`, module permissions | `RequirePermission` and tenant-aware `GetCompanyId` in backend | Route guards in `App.tsx` and `AppShell` | Tenant-scoped | Source inspection | Low | Verified |
| Platform admin routes | Platform admin roles / platform auth | Separate `PlatformApp` / platform endpoints | Separate shell and login | Platform-scoped | Source inspection | Low | Verified |
| Customer portal routes | `customer_portal:view`, `customer_portal:manage` | Backend customer portal guards | Customer portal pages remain scoped | Tenant/customer scoped | Source inspection | Low | Verified |
| Safety / maintenance endpoints | `safety:*`, `maintenance:*` | Backend route guards | Visible pages use live clients | Tenant-scoped | Source inspection + build | Low | Verified |
| Telemetry / live map endpoints | `telemetry:*` | Backend route guards | Live map and control tower pages | Tenant-scoped | Source inspection + build | Low | Verified |
| Finance endpoints | `finance:*`, `billing:*` | Backend route guards | Finance pages visible only to permitted users | Tenant-scoped | Source inspection | Low | Verified |
| User / role / admin endpoints | `users:*`, `roles:*` | Backend route guards | Admin page tabs are permission-gated | Tenant-aware | Source inspection + new admin permissions endpoint | Low | Verified |
| AI recommendations / actions | AI recommendation permissions | Backend recommendations remain read-only | Frontend AI surfaces stay assistive | Tenant-scoped | Source inspection | Medium | Verified |

