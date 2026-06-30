# Stage 9 API Contracts

## Smart assignment

- `GET /api/jobs/{jobId}/smart-assign/recommendations`
- `POST /api/jobs/{jobId}/smart-assign/recommend`
- `POST /api/smart-assign/recommendations/{id}/accept`
- `POST /api/smart-assign/recommendations/{id}/reject`

## Site access

- `GET /api/jobs/{jobId}/site-access`
- `POST /api/jobs/{jobId}/site-access`
- `PATCH /api/site-access/{id}`

## Access documents

- `GET /api/jobs/{jobId}/access-documents`
- `POST /api/jobs/{jobId}/access-documents`
- `PATCH /api/access-documents/{id}/status`

## Pickup authorization

- `GET /api/jobs/{jobId}/pickup-authorizations`
- `POST /api/jobs/{jobId}/pickup-authorizations`
- `PATCH /api/pickup-authorizations/{id}`

## Warehouse handover

- `GET /api/jobs/{jobId}/warehouse-handovers`
- `POST /api/jobs/{jobId}/warehouse-handovers`
- `PATCH /api/warehouse-handovers/{id}`

## Proof packages

- `GET /api/jobs/{jobId}/proof-packages`
- `POST /api/jobs/{jobId}/proof-packages`
- `GET /api/proof-packages/{id}`
- `PATCH /api/proof-packages/{id}`
- `POST /api/proof-packages/{id}/submit`
- `POST /api/proof-packages/{id}/validate`
- `GET /api/proof-packages/{proofPackageId}/artifacts`
- `POST /api/proof-packages/{proofPackageId}/artifacts`
- `GET /api/proof-packages/{proofPackageId}/billing-confidence`

## Contract notes

- Every route is permission-gated through `RequirePermission(...)`.
- Every route resolves tenant/company context on the backend.
- Create actions accept idempotency keys where retried mobile submits are expected.
- Approval-required responses return `202 Accepted` with an approval request reference instead of silently completing the business effect.

