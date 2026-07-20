# Opstrax Stage 10 Mobile API Contracts

## 1. Authentication Model
- Mobile and web clients use the same bearer session model.
- Tenant context comes from the authenticated session and `X-Opstrax-Tenant-Id` header.
- There is no mobile-only auth shortcut.
- All write actions remain behind centralized RBAC and tenant-scoped authorization.

## 2. Tenant / Company Resolution
- Backend resolves tenant/company from the current session.
- Requests without tenant context fail closed.
- Cross-tenant reads and writes are not permitted.

## 3. RBAC Enforcement
- Stage 10 endpoints use centralized `RequirePermission`.
- Frontend visibility mirrors backend permission aliases only.
- The UI never treats hidden buttons as security.

## 4. Driver / Operator Routes
- Route family: `/api/operations/jobs/{jobId}/execution-summary`
- Route family: `/api/jobs/{jobId}/smart-assign/recommendations`
- Route family: `/api/smart-assign/recommendations/{id}/accept|reject`
- Route family: `/api/jobs/{jobId}/proof-packages`

## 5. Field Worker Routes
- Route family: `/api/jobs/{jobId}/site-access`
- Route family: `/api/jobs/{jobId}/access-documents`
- Route family: `/api/jobs/{jobId}/proof-packages`
- Route family: `/api/proof-packages/{id}/artifacts`

## 6. Dispatcher / Supervisor Routes
- Route family: `/api/operations/jobs/{jobId}/execution-summary`
- Route family: `/api/jobs/{jobId}/smart-assign/recommendations`
- Route family: `/api/jobs/{jobId}/site-access`
- Route family: `/api/jobs/{jobId}/warehouse-handovers`

## 7. Warehouse User Routes
- Route family: `/api/jobs/{jobId}/pickup-authorizations`
- Route family: `/api/jobs/{jobId}/warehouse-handovers`

## 8. Third-Party Pickup Routes
- Route family: `/api/jobs/{jobId}/pickup-authorizations`
- Route family: `/api/pickup-authorizations/{id}`

## 9. Customer / Client Routes
- Route family: `/api/proof-packages/{id}`
- Route family: `/api/proof-packages/{id}/validate`
- Customer access remains read-scoped and tenant-scoped.

## 10. Offline / Idempotency Strategy
- Writes should carry `idempotencyKey` or `Idempotency-Key`.
- Mobile retries must be safe and deterministic.
- `clientGeneratedId` is preserved where the Stage 9 contract already supports it.
- Full offline sync is not built in Stage 10.

## 11. Evidence Upload Strategy
- Stage 10 stores evidence metadata and references.
- File-service gaps are shown honestly when a file payload is unavailable.
- Proof artifacts can include `deviceId`, `capturedAt`, `uploadedAt`, and geo metadata.

## 12. Device / Location Metadata
- Supported fields include `deviceId`, `mobileAppVersion`, `geoLatitude`, `geoLongitude`, `capturedAt`, and `uploadedAt`.
- These are carried through proof, pickup, warehouse, and access payloads.

## 13. Future Push Notification Mapping
- `smart_assignment.recommended`
- `site_access.required`
- `pickup_authorization.verified`
- `warehouse_handover.completed`
- `proof_package.submitted`
- `proof_package.validated`
- `billing_confidence.updated`

## 14. Error Response Standard
- API responses use the existing `ApiResponse<T>` envelope.
- Authorization errors return fail-closed behavior with clear messages.
- 401 and 403 must be handled gracefully by the client.

## 15. Pagination / Filtering Standard
- Job-scoped list routes may accept job filters and simple list scopes.
- The mobile surface should not assume unbounded lists.

## 16. Data Minimization / Role-Scoped Fields
- Return only the fields needed for the current role and view.
- Internal-only fields stay behind higher permissions.
- The execution summary is a read model, not a business mutation surface.
