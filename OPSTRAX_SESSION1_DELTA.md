# Session 1 — A.1 gap fixes (delta report)

Closed the genuine cross-tenant gaps from `OPSTRAX_PHASE0_VERIFICATION.md` Part
A.1, reusing existing patterns only (`RequirePermission` + `GetCompanyId` +
`WHERE company_id=@cid`, modeled on `UpdateVehicle`). `dotnet build` + `dotnet
test` run after each batch.

## Build / test

| Stage | Build | Tests |
|---|---|---|
| After write-gap fixes (#3/#4 + drivers siblings) | succeeded, 0 errors | 862 passed, 0 failed, 0 skipped |
| After read-gap fixes (#5–#10) | succeeded, 0 errors | 862 passed, 0 failed, 0 skipped |

No tests modified, none skipped.

## Before / after — the 6 named routes

| Route | Before (authn / RBAC / tenant) | After | Fix |
|---|---|---|---|
| `POST /api/vehicles/{id}/assign-driver` | yes / **no** / **no** (write) | yes / **yes** `fleet:manage` / **yes** | `ChangeEntityStatus` now takes a permission, calls `RequirePermission`, scopes `UPDATE … WHERE id=@id AND company_id=@cid`, returns 404 on 0 rows, scopes the related driver update, and inserts `vehicle_assignments` with the session `company_id` (was hardcoded `1`) |
| `POST /api/vehicles/{id}/change-status` | yes / **no** / **no** (write) | yes / **yes** `fleet:manage` / **yes** | `ChangeStatus` now `RequirePermission` + `WHERE id=@id AND company_id=@cid` + 404 on 0 rows |
| `GET /api/carbon-emissions` | yes / no / **no** (read) | yes / no / **yes** | added `HttpContext` + `WHERE v.company_id=@cid` (tenant-filter only, per instruction) |
| `GET /api/profitability` | yes / **no** / yes | yes / **yes** `finance:view` / yes | added `RequirePermission` matching the `/profitability` page gate |
| `GET /api/feature-flags` | yes / **no** / yes | yes / **yes** `users:manage` / yes | added `RequirePermission` matching the `/feature-flags` page gate |
| `GET /api/integrations` | yes / **no** / yes | yes / **yes** `telematics:providers:manage` / yes | added `RequirePermission` matching the `/integrations` page gate |
| `GET /api/routes/summary` | yes / yes `dispatch:view` / **no** | yes / yes / **yes** | added `WHERE company_id=@cid` |
| `GET /api/last-mile/deliveries` | yes / yes `dispatch:view` / **no** | yes / yes / **yes** | added `WHERE r.company_id=@cid` (downstream stop queries already scoped via the now-tenant-filtered route IDs) |

(RBAC permission keys were chosen to match each page's existing frontend
`RequirePermission` gate, so no legitimate user is newly 403'd. `fleet:manage` is
the key already used by `UpdateVehicle`/`UpdateDriver`.)

## Also fixed (same shared helpers, identical bug — unavoidable and correct)

`ChangeStatus` / `ChangeEntityStatus` are shared, so the same fix also closed the
identical cross-tenant write gap on:
- `POST /api/drivers/{id}/assign-vehicle`
- `POST /api/drivers/{id}/change-status`

## Additional latent gap found, NOT fixed (flagged, out of named scope)

The reference the request cited — `GET /api/fleet/utilization/summary` (and
`GET /api/fleet/utilization`) — is **itself unscoped** (`FROM vehicles WHERE
deleted_at IS NULL`, no `company_id`, no `HttpContext`). It has the same
cross-tenant-read bug. I did **not** modify it (not in the named list); applied
the correct tenant pattern to `carbon-emissions` directly instead. Recommend
adding `utilization` + `utilization/summary` to the backlog.

## Files changed
- `backend-dotnet/Controllers/EndpointMappings.cs` (only file)
