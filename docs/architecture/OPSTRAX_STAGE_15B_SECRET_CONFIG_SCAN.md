# Stage 15B Secret / Config Scan

| Pattern | File | Finding Type | Secret? Yes/No/Unsure | Risk | Action Taken | Final Status |
|---|---|---|---|---|---|---|
| `password` / `connection string` | `.env` | Local environment values include a real-looking Postgres connection string. | Yes, local-only | Medium | Redacted from this report; not added to the commit set. | Not committed |
| `password` / `demo_password` | `database/init/001_schema.sql`, `backend-dotnet/Controllers/EndpointMappings.cs` | Demo/bootstrap fields for seeded local auth. | No, by design | Low | Documented as legacy/demo bootstrap, not production secret material. | Accepted |
| `token` / `bearer` | auth/session code across backend/frontend/mobile | Normal auth/session handling. | No | Low | Reviewed for accidental logging or query-string leakage. | Accepted |
| `jwt` / `signingkey` | `backend-dotnet/Services/ConfigValidationService.cs` and docs | Configuration validation only. | No | Low | Confirmed validation code does not print the secret value. | Accepted |
| `openai` / `ollama` / `twilio` / `sendgrid` / cloud provider patterns | backend config and docs | Provider references and placeholders. | No | Low | Kept as integration placeholders only. | Accepted |

Notes:
- The local `.env` file is present in the workspace and should stay out of any push.
- No tracked source diff introduced a new secret in this pass.

