# Stage 14B Test Coverage

| Test / File | Coverage | Result | Risk Covered | Remaining Gap |
| --- | --- | --- | --- | --- |
| `backend-dotnet.Tests/Stage13SourceRegressionTests.cs` | Dashboard naming, live-only clients, no fake fallback in touched services | Updated in this pass | Prevents reintroduction of fake-success / seed masking | None for this slice |
| `dotnet build backend-dotnet/Opstrax.Api.csproj` | Backend compile safety | Passed | Build regressions | None for this slice |
| `dotnet test backend-dotnet.Tests/Opstrax.Tests.csproj --no-restore` | Backend regression suite | Passed with `847` tests | Runtime regressions | None for this slice |
| `npm run build` in `frontend/` | Frontend compile safety | Passed | Route / component regressions | None for this slice |
| `npm run lint` in `frontend/` | Frontend style / correctness | Passed | Quality regressions | None for this slice |
| `npm run build` in `backend/` | Node backend compile safety | Passed | Type / build regressions | None for this slice |
