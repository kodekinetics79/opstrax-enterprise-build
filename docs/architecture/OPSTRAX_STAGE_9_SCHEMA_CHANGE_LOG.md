# Stage 9 Schema Change Log

## Added tables

- `smart_assignment_recommendations`
- `assignment_confirmations`
- `site_access_requirements`
- `access_documents`
- `pickup_authorizations`
- `warehouse_handovers`
- `proof_packages`
- `proof_artifacts`
- `billing_confidence_records`

## Important columns

- `company_id` is present on every tenant-owned Stage 9 table.
- `correlation_id` and `causation_id` are persisted on the operational records that need traceability.
- `idempotency_key` is present where repeated mobile submits are expected.
- `status` columns exist for approval-safe workflows and operational transitions.
- `metadata_json` exists on the evidence-bearing tables to keep the model forward-compatible.

## Index and constraint intent

- Indexes favor `company_id`, `job_id`, `trip_id`, `status`, and `created_at` access patterns.
- Idempotency-oriented uniqueness is used where duplicate mobile submits would otherwise double-write records.
- Proof artifacts are indexed back to their package so validation can find evidence quickly.

## Migration posture

- All Stage 9 schema changes are additive.
- No destructive migration was required for this slice.
- The schema can be rolled back by dropping the new Stage 9 tables if a local reset is needed.

