# Finance UI / Billing / Invoices / AR

## Current State
- `FinancialAnalyticsPage.tsx` covers invoices, payments, and profitability.
- Export behavior now uses live API rows instead of seed-generated rows.

## Productization Notes
- Finance screens should keep a clear boundary between live account state and any demo scaffolding.
- Error states must stay truthful so a missing backend does not look like zero finance activity.

## Remaining Gaps
- AR/collections depth is still lighter than the operational fleet modules.
- The page is still a reporting surface, not a full finance workspace.

## Verdict
- Productization is solid for this stage after the export fix, with medium residual scope risk.
