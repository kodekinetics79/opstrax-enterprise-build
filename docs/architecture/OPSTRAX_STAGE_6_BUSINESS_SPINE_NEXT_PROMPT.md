# Stage 6 Next Prompt

Use this prompt for the next slice only. Do not execute it yet.

## Objective

Continue the commercial spine by connecting the new canonical business tables to the existing operational flow without building full CRM or finance.

## Next Slice Goals

1. Align contract and rate-card persistence so the canonical `rate_cards` table becomes the preferred write path.
2. Add a minimal compatibility bridge from existing contract-rate screens to the canonical business spine.
3. Generate `job_charges` from approved job/trip activity in a deterministic, tenant-scoped way.
4. Preserve correlation, causation, and audit metadata through the charge path.
5. Add tests for rate-card compatibility and charge generation.
6. Keep the UI untouched unless a tiny label/config readout is required.

## Hard Limits

- Do not build full CRM.
- Do not build invoicing, payments, or AR aging.
- Do not redesign the frontend.
- Do not build full AI automation.
- Do not build full IoT ingestion.
- Do not push, deploy, or touch production.

## Guardrails

- Keep the business profile generic by default.
- Keep tenant isolation explicit.
- Use the canonical business tables instead of hardcoding vertical-specific labels in business logic.
- Do not remove the existing compatibility surfaces until the canonical flow is proven.

## Expected Deliverables

1. Compatibility bridge implementation
2. Local migration only if a new additive column or index is truly required
3. Tests
4. Completion report

## Success Criteria

- Existing commercial flows continue to work.
- Canonical rate cards and job charges are the preferred durable path.
- The next business slice can start from a cleaner, less fleet-specific foundation.
