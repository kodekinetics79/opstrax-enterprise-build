# Stage 7 P0-B1C API Contracts

## Job readiness

- `POST /api/jobs/{jobId}/mark-ready-to-bill`
- Permission: `finance.job.ready_to_bill`
- Result: marks a completed billable job ready for invoice drafting or returns a leakage recommendation path.

## Invoice drafts

- `GET /api/invoice-drafts`
- `GET /api/invoice-drafts/{id}`
- `POST /api/jobs/{jobId}/invoice-draft`
- `PATCH /api/invoice-drafts/{id}`
- Permissions: `finance.invoice_draft.read`, `finance.invoice_draft.create`, `finance.invoice_draft.update`
- `Idempotency-Key` is supported on draft creation.

## Revenue summaries

- `GET /api/revenue/summary`
- Permission: `finance.revenue.summary.read`

## Customer summaries

- `GET /api/customers/{customerId}/summary`
- Permission: `customer.account.summary.read`

## Approval behavior

- Active rate-card changes that materially alter pricing return `202 Accepted` with an approval request.
- Invoice draft updates can also return approval-required responses.
- Approval is tenant-scoped and does not directly execute the business action in this slice.

## Response shape

- All endpoints return the repo's standard `ApiResponse<object>` envelope.
- Reads are tenant-scoped through the authenticated company context.
