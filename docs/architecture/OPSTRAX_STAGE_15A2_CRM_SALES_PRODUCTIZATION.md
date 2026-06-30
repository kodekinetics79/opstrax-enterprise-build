# CRM / Sales / Quote-to-Contract

## Current State
- `CustomersPage.tsx`, `LeadsPage.tsx`, `OpportunitiesPage.tsx`, `ContractsPage.tsx`, `QuotationsPage.tsx`, and `RateCardsPage.tsx` already present a coherent commercial workflow.
- The shared live data layer does not silently fall back to fabricated runtime rows.

## Productization Notes
- Keep lead/opportunity/customer narratives connected so the user can move from prospect to commercial record without switching mental models.
- Preserve the current live query pattern and avoid turning exports into seed-backed demos.

## Remaining Gaps
- The commercial story is still distributed across multiple pages rather than one guided flow.
- Some surfaces are richer than others, so the product narrative should stay honest about partially mature subflows.

## Verdict
- CRM and sales are productized enough for the current stage, with medium residual polish risk.
