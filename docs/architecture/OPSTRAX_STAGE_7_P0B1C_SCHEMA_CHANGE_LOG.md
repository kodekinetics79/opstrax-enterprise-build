# Stage 7 P0-B1C Schema Change Log

## Runtime additions

- `invoice_drafts`
- `invoice_draft_lines`

## Notes

- The revenue-readiness tables are created idempotently by `RevenueReadinessSchemaService`.
- No destructive migration was added for this stage.
- No production migration was run.
- The schema stays tenant-scoped through `company_id` on revenue-owned rows.

## Revenue draft shape

- Draft headers store customer, contract, job, status, currency, subtotal, tax, total, and metadata.
- Draft lines store charge provenance, quantities, unit rates, amounts, and optional metadata.
- Indexes support tenant lookup, status lookup, job lookup, and line lookup.

## Compatibility note

- The local revenue tests also patch a few older disposable-DB columns for rerun safety.
- That compatibility work is test-harness-only and should not be treated as a production schema migration.
