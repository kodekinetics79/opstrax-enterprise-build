# ⚠️ OPERATIONAL ACTION REQUIRED — Neon Database Password Rotation

**Status: OPEN — must be completed by a human operator before pilot go-live.**
**Raised:** 2026-07-02 (platform-admin hardening pass)

## What happened

During an earlier working session, the Neon Postgres password was exposed in a
terminal/conversation context. The credential itself is **not** committed to this
repository (verified: `.env`, `backend/.env`, `backend-dotnet/appsettings.json`,
`frontend/.env` are all gitignored and untracked; `.env.example` and
`api-dotnet/appsettings.example.json` contain placeholders only). However,
exposure outside the repo is sufficient reason to rotate.

## Required actions (in order)

1. **Neon console** → project → Roles → rotate every role that may have shared or
   derived exposure. Keep the owner/migration identity out of runtime services.
2. **Render** (backend `opstrax-enterprise-build-*` service) → Environment →
   update the distinct restricted `PG_CONNECTION_APP` and `PG_CONNECTION_SYSTEM`
   secret values → redeploy the approved exact-SHA candidate.
3. **Vercel** (frontend project) → only if any DB-derived secret is stored there
   (frontend should not hold DB credentials; verify none exist).
4. **Local `.env` files** → update on developer machines.
5. Verify the old password no longer authenticates (`psql` with old credential
   must fail).
6. Verify the healthy runtime reports distinct restricted app/system identities,
   never the owner identity, and record UTC, operator and provider audit reference below.

## Rules

- Never paste the new password into a chat session, commit, log, or ticket.
- Prefer a dedicated least-privilege app role over `neondb_owner` when rotating.

| UTC date/time | Operator | Provider audit/reference | Action |
|---|---|---|---|
| _pending_ | _pending_ | _pending_ | Rotation not yet performed |
