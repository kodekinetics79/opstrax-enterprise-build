# Stage 7 CRM / Sales / Marketing / Invoicing Expansion Plan

## Next vertical layers

1. Customer master and contact lifecycle
2. Sales pipeline and opportunity tracking
3. Quote generation and quote approvals
4. Contract versioning and amendments
5. Rate card variants by vertical and service type
6. Invoice issuance and credit note foundation
7. AR aging and collections visibility
8. Renewal and churn-risk signals
9. Campaign and commission foundations

## Guardrails

- Keep tenant isolation first.
- Keep AI deterministic, auditable, and approval-aware.
- Do not let AI write business tables directly.
- Reuse the existing foundation services instead of introducing another parallel persistence model.

## Delivery order

- Revenue activation should come before full CRM breadth.
- Finance issuance should come before advanced AR tooling.
- Marketing automation should stay behind the same authorization and audit rails.
