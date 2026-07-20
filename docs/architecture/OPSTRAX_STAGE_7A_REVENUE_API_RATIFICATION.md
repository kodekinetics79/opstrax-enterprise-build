# Stage 7A Revenue API Ratification

| Endpoint | Permission | Status | Evidence | Gap | Follow-Up |
|---|---|---|---|---|---|
| `POST /api/jobs/{jobId}/mark-ready-to-bill` | `finance.job.ready_to_bill` | Ratified | `backend-dotnet/Controllers/RevenueReadinessEndpoints.cs`; covered by `RevenueReadinessPostgresTests.MarkReadyToBill_WithCharges_Succeeds_And_Writes_Event`. | None for this slice. | Carry forward to finance activation. |
| `GET /api/invoice-drafts` | `finance.invoice_draft.read` | Ratified | Endpoint exists and returns tenant-scoped drafts. | None. | Add pagination later if volume grows. |
| `GET /api/invoice-drafts/{id}` | `finance.invoice_draft.read` | Ratified | Endpoint loads by company context. | None. | Keep tenant isolation strict. |
| `POST /api/jobs/{jobId}/invoice-draft` | `finance.invoice_draft.create` | Ratified | Supports `Idempotency-Key` and duplicate-safe replay. | No final invoice issuance yet. | Use this as the draft-to-issue precursor. |
| `PATCH /api/invoice-drafts/{id}` | `finance.invoice_draft.update` | Ratified | Approval-required path returns `202 Accepted`. | No invoice approval flow yet. | Build invoice approval in Stage 8. |
| `GET /api/revenue/summary` | `finance.revenue.summary.read` | Ratified | Returns tenant-scoped revenue rollup. | No AR aging. | Add finance activation summaries later. |
| `GET /api/customers/{customerId}/summary` | `customer.account.summary.read` | Ratified | Returns tenant-scoped customer 360 summary. | No customer portal UI change. | Reuse for CRM/finance expansion. |

## Verdict

All Stage 7 revenue endpoints are present, tenant-scoped, and governed. The next slice can safely build on them.
