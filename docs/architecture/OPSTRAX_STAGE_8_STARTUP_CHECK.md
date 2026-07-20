# Stage 8 Startup Check

| Area | Finding | Evidence | Risk | Action |
|---|---|---|---|---|
| Finance activation schema | Added as a local/dev schema service and additive SQL contract. | `backend-dotnet/Services/FinanceActivationSchemaService.cs`; `database/migrations/2026_06_28_stage8_finance_activation.sql` | Low | Keep production mutation disabled by default. |
| Production safety | Revenue and finance schema startup remain guarded behind config defaults that resolve to off in production. | `backend-dotnet/Program.cs` | Low | Leave `RevenueReadinessSchema:Enabled` and `FinanceActivationSchema:Enabled` off unless explicitly enabled. |
| Invoice approval | High-risk invoice issue now creates approval requests instead of issuing directly. | `backend-dotnet/Services/RevenueReadinessService.cs`; `backend-dotnet/Controllers/RevenueReadinessEndpoints.cs` | Medium | Approval decision path is intentionally minimal and should stay tenant-scoped. |
| Issued invoices | Persistent issued invoice, line, and payment tables now exist. | `backend-dotnet/Services/FinanceActivationSchemaService.cs` | Low | Keep the one-to-one draft-to-issued constraint. |
| Runtime proof | Local build and test loop passed after the finance slice. | `dotnet build`; `dotnet test`; frontend and backend builds | Low | Re-run the same proof loop after future finance changes. |
| Residual gaps | No payment gateway, credit note, dispute, or full AR engine yet. | repo scope | Medium | Treat these as Stage 9+ work, not Stage 8 regressions. |
