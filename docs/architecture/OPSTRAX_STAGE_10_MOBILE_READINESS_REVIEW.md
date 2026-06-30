# Opstrax Stage 10 Mobile Readiness Review

| Role | Route Families | Key Permissions | Offline / Idempotency | Evidence / Location Metadata | Future Notification Events | Status |
|---|---|---|---|---|---|---|
| Driver / Operator | `/api/operations/jobs/{jobId}/execution-summary`, `/api/jobs/{jobId}/proof-packages` | `operations.execution_summary.read`, `operations.proof.create`, `operations.proof.submit` | Idempotency supported on write paths; no full offline sync | Supported on proof and artifact payloads | `proof_package.submitted`, `proof_package.validated` | Ready for contract-driven mobile shell |
| Field Worker / Cleaner / Technician / Guard | `/api/jobs/{jobId}/site-access`, `/api/jobs/{jobId}/access-documents`, `/api/jobs/{jobId}/proof-packages` | `operations.site_access.create`, `operations.access_document.create`, `operations.proof_artifact.create` | Retry-safe writes required | `deviceId`, geo, `capturedAt`, `uploadedAt` | `site_access.required`, `proof_package.submitted` | Ready for contract-driven mobile shell |
| Dispatcher / Supervisor | `/api/operations/jobs/{jobId}/execution-summary`, `/api/jobs/{jobId}/smart-assign/recommendations` | `operations.execution_summary.read`, `dispatch.smart_assign.accept`, `dispatch.smart_assign.reject` | No unsafe automatic mutation | Recommendation and assignment metadata preserved | `smart_assignment.recommended`, `billing_confidence.updated` | Ready for control-plane mobile companion |
| Warehouse User | `/api/jobs/{jobId}/pickup-authorizations`, `/api/jobs/{jobId}/warehouse-handovers` | `operations.pickup_authorization.verify`, `operations.warehouse_handover.update` | Idempotent create/update path needed | Warehouse and handover metadata supported | `pickup_authorization.verified`, `warehouse_handover.completed` | Ready |
| Third-Party Pickup User | `/api/pickup-authorizations/{id}` | `operations.pickup_authorization.verify` | Verification must not duplicate effect | Identity and validity fields supported | `pickup_authorization.verified` | Ready |
| Customer / Client User | `/api/proof-packages/{id}`, `/api/proof-packages/{id}/validate` | `operations.proof.read`, `customer_portal:view` | Read-heavy; no write shortcuts | Proof package and validation data surfaced minimally | `proof_package.validated` | Ready for read-only preview |

## Review Notes
- Mobile routes are contract-ready, not a native app.
- The current design supports future Expo/React Native work without changing the backend contract.
- No mobile-only auth shortcut was introduced.
