# Stage 15A Test Coverage

| Test/File | Coverage | Result | Risk Covered | Remaining Gap |
|---|---|---|---|---|
| `backend-dotnet.Tests/Stage15SourceRegressionTests.cs` | Trips route/page/nav/dashboard wiring | Added | Prevents silent removal of the new P0 route | Runtime UI behavior still relies on manual verification |
| `backend-dotnet.Tests/Stage13SourceRegressionTests.cs` | Dashboard naming + live-only surfaces | Existing | Prevents Cockpit rename regression and fallback masking | Broader fallback cleanup remains |
| Backend build | Compile verification | Passed | Catches route/service wiring issues | Runtime data still needs the live DB |
| Frontend build | Type/build verification | Passed after Trips fixes | Catches TS/route issues | None for build |
| Backend tests | Full suite | Passed (849/849) | Confirms regression safety across the main app and foundation slices | Remaining partial modules still need future productization |
| Frontend lint | Static quality check | Passed | Catches style/import issues | None for build path |
