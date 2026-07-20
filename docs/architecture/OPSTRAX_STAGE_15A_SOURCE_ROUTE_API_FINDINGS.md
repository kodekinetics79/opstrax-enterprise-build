# Stage 15A Source / Route / API Findings

| Area | Finding | File/Route/API | Risk | Fix Applied | Deferred Reason | Final Status |
|---|---|---|---|---|---|---|
| Trips route | Dedicated visible trips page was missing from the main app. | `frontend/src/App.tsx`, `/trips` | High | Added lazy page and route. | None | Fixed |
| Trips nav | Trips was not visible in the main dispatch nav. | `frontend/src/layouts/AppShell.tsx`, `frontend/src/modules/moduleConfig.ts` | High | Added Trips module + nav item. | None | Fixed |
| Dashboard surface | Dashboard did not provide a direct trips shortcut. | `frontend/src/pages/CommandCenterPage.tsx` | Medium | Added operational shortcut panel. | None | Improved |
| Trip API | Trip API already existed and was live-only. | `frontend/src/services/tripApi.ts` | Low | Reused it. | None | Good |
| Trip backend | Trip list/detail/compliance/action endpoints already existed. | `backend-dotnet/Controllers/EndpointMappings.cs` `/api/trips` | Low | No backend change required. | None | Good |
| Legacy fallback residue | Some non-trips services still contain legacy fallback callbacks/seed scaffolding in source. | `frontend/src/services/complianceApi.ts`, `frontend/src/pages/*` | Medium | Did not expand those surfaces in this pass. | Larger cleanup is separate. | Remaining gap |
| Admin/customer boundaries | Existing isolated platform shell and customer portal routes remain intact. | `frontend/src/pages/platform/PlatformApp.tsx`, `frontend/src/App.tsx` | Medium | Left boundaries intact. | None | Good |
| RBAC | Visible route guards still fail closed through shared permission checks. | `frontend/src/hooks/usePermission.tsx`, `frontend/src/auth/rbacConfig.ts` | Medium | No weakening introduced. | None | Good |

