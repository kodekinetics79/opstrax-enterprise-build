# OpsTrax No Fake Data Policy

## Policy
- Demo data is allowed only in seed/dev mode.
- Production-facing screens must show real API-backed state.
- Missing APIs must show empty, loading, or error states, not fake data.
- Any fake data must be clearly labeled and removable.

## Enforcement
- Keep demo paths isolated from production logic.
- Prefer empty state UX over fabricated records when APIs are absent.

