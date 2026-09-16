# OpsTrax UX Contract

## Commercial lifecycle

The commercial workspace follows persisted records through Lead → Opportunity → Quote → Customer → Contract → Job → Trip → Proof of Delivery → Charge → Invoice → Payment. A route may link to the next stage only when that workflow exists and the current user has its permission.

Lead, opportunity, quotation, and contract registers remain independently saved workflows. The interface must disclose where conversion is not automated and must never imply that opening a downstream form changed an upstream record.

## Financial actions

Invoice and payment actions follow `docs/architecture/ADR-007-revenue-order-to-cash.md` and `docs/architecture/ADR-008-financial-platform.md`.

- Draft preparation requires a customer-linked terminal job and persisted charges in one known currency.
- Invoice issue and approval are permission-gated and preserve separation of duties.
- A payment record requires confirmed funds, a positive amount that does not exceed the outstanding balance, currency, method, and reconciliation reference.
- Provider settlement is shown only when evidenced.
- Revenue and costs are never combined across currencies. Margin remains unavailable without qualified cost evidence.

## List behavior

- Summary measures and filter counts derive from the same loaded records or an explicitly authorized summary endpoint.
- Selecting a metric or status filter updates the visible list and exposes its active state.
- Search and filters compose; clearing them restores the complete authorized result.
- Empty states distinguish no records, no filter matches, missing permission, and load failure.
- Export uses the current authorized, filtered result when the page offers filtered export.
- Rows open a useful inspector or workflow. Destructive and financial actions use explicit confirmation or validated dialogs.

## Failure and truth states

- Loading, empty, unavailable, permission-denied, validation, and server-error states are visually distinct.
- Missing data is not displayed as zero, healthy, current, connected, compliant, verified, or settled.
- Mutations report progress, success, and actionable failure. Ambiguous outcomes require a refresh before repeating a destructive or financial action.
