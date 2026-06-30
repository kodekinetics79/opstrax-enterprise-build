# Stage 7 P0-B1C Rollback Notes

## What to revert locally

- Remove `backend-dotnet/Controllers/RevenueReadinessEndpoints.cs`
- Remove `backend-dotnet/Services/RevenueReadinessSchemaService.cs`
- Remove `backend-dotnet/Services/RevenueReadinessService.cs`
- Remove Stage 7 permission aliases from `backend-dotnet/Foundation/FoundationServices.cs`
- Remove the revenue endpoint mapping from `backend-dotnet/Program.cs`
- Remove the Stage 7 revenue tests and docs if you are reverting the feature slice entirely

## Schema rollback

- If a local DB rollback is needed, drop:
  - `invoice_drafts`
  - `invoice_draft_lines`
- The formal review-only contract lives at `database/migrations/2026_06_28_stage7a_revenue_readiness_schema_contract.sql`.
- Do not touch production.
- Do not run any destructive SQL outside the disposable local DB.

## Safety note

- No production migration was applied for this stage.
- The runtime changes are additive, so rollback should be a local code revert plus optional local table drop only.
