# Opstrax Stage 10 API Contracts

## Read Model
- `GET /api/operations/jobs/{jobId}/execution-summary`
- Permission: `operations.execution_summary.read`
- Returns:
  - `job_id`
  - `trip_id`
  - `assignment_summary`
  - `smart_assignment_summary`
  - `site_access_summary`
  - `access_document_summary`
  - `pickup_authorization_summary`
  - `warehouse_handover_summary`
  - `proof_package_summary`
  - `proof_artifact_summary`
  - `validation_summary`
  - `billing_confidence_summary`
  - `risk_summary`
  - `next_best_actions`
  - `mobile_ready_actions`

## Stage 9 Workflow Endpoints Used by Stage 10 UI
- `GET /api/jobs/{jobId}/smart-assign/recommendations`
- `POST /api/jobs/{jobId}/smart-assign/recommend`
- `POST /api/smart-assign/recommendations/{id}/accept`
- `POST /api/smart-assign/recommendations/{id}/reject`
- `GET /api/jobs/{jobId}/site-access`
- `POST /api/jobs/{jobId}/site-access`
- `PATCH /api/site-access/{id}`
- `GET /api/jobs/{jobId}/access-documents`
- `POST /api/jobs/{jobId}/access-documents`
- `PATCH /api/access-documents/{id}/status`
- `GET /api/jobs/{jobId}/pickup-authorizations`
- `POST /api/jobs/{jobId}/pickup-authorizations`
- `PATCH /api/pickup-authorizations/{id}`
- `GET /api/jobs/{jobId}/warehouse-handovers`
- `POST /api/jobs/{jobId}/warehouse-handovers`
- `PATCH /api/warehouse-handovers/{id}`
- `GET /api/jobs/{jobId}/proof-packages`
- `POST /api/jobs/{jobId}/proof-packages`
- `GET /api/proof-packages/{id}`
- `PATCH /api/proof-packages/{id}`
- `POST /api/proof-packages/{id}/submit`
- `POST /api/proof-packages/{id}/validate`
- `GET /api/proof-packages/{proofPackageId}/artifacts`
- `POST /api/proof-packages/{proofPackageId}/artifacts`
- `GET /api/proof-packages/{proofPackageId}/billing-confidence`

## Contract Notes
- No endpoint in Stage 10 permits AI to assign, validate, complete, or issue business actions directly.
- The UI uses the same backend contract for demo and future mobile planning.
- No fake data is used to hide missing backend wiring.
