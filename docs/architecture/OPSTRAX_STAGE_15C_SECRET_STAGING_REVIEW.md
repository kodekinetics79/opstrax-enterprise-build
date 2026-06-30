# Stage 15C Secret Staging Review

| File | Finding | Secret Risk | Currently Tracked? | Should Stage? | Required Action |
|---|---|---|---|---|---|
| `.env` | Contains a real-looking Postgres connection string with host/user/password fields. | High | No | No | Keep untracked, do not stage, and rotate if ever shared outside the machine. |
| `.env.example` | Example placeholder values only. | Low | Yes | No | Keep as documentation; do not overwrite with real values. |
| `backend-dotnet/Program.cs` | References auth/session/security config; no secret values surfaced in this review. | Low | Yes | Yes | Safe to stage only as existing source diff. |
| `backend-dotnet/Services/ConfigValidationService.cs` | Validates presence/strength of keys without printing values. | Low | Yes | Yes | Safe to stage only as existing source diff. |
| `frontend/src/services/apiClient.ts` | Uses bearer/CSRF session tokens in memory. | Low | Yes | Yes | Safe to stage only as existing source diff. |
| `frontend/src/services/platformApi.ts` | Uses bearer/CSRF session tokens in memory. | Low | Yes | Yes | Safe to stage only as existing source diff. |
| `mobile/src/api/client.ts` | Uses bearer session token in memory. | Low | No | No | Mobile workspace is excluded from this stage. |
| `docs/LOGIN_RBAC_CSRF.md` | Describes token/CSRF behavior in documentation only. | Low | Yes | Yes | Safe to stage as docs, if included later. |

Notes:
- No tracked secret value was introduced in this pass.
- The local `.env` remains the main blocking hygiene risk and must stay out of the staged set.

