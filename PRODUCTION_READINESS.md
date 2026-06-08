# Production Readiness

## Current posture

- Frontend: Vite app with auth, RBAC, error boundary, and permission-blocked states.
- Backend: .NET API with auth middleware, readiness endpoint, safer error handling, and basic rate limiting.
- Node API: Express utility service with consistent health/readiness responses and request throttling.

## Operational checks

- `frontend`: `npm run build`
- `backend`: `npm run build`
- `backend-dotnet`: `dotnet build backend-dotnet/Opstrax.Api.csproj`

## Recovery notes

- Database: restore from MySQL backups before replaying seed/migration scripts.
- Object storage: keep receipts, POD assets, dashcam evidence, and exports versioned and tenant-scoped.
- Rollback: deploy the previous frontend build and last known good API container/image.

## UAT checklist

- Login succeeds for the expected roles.
- Unauthorized users are blocked from protected routes.
- Core list/detail pages load for vehicles, drivers, jobs, customers, and shipments.
- Save/update actions persist where wired.
- Health and readiness endpoints return green.
- Error states and permission-denied states render without blank screens.
