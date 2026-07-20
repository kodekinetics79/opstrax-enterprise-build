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

1. **Neon console** → project → Roles → reset the password for the application
   role (`neondb_owner` or the dedicated app role).
2. **Render** (backend `opstrax-enterprise-build-*` service) → Environment →
   update the Postgres connection string / `PG_CONNECTION` value → redeploy.
3. **Vercel** (frontend project) → only if any DB-derived secret is stored there
   (frontend should not hold DB credentials; verify none exist).
4. **Local `.env` files** → update on developer machines.
5. Verify the old password no longer authenticates (`psql` with old credential
   must fail).
6. Record rotation date/operator below.

## Rules

- Never paste the new password into a chat session, commit, log, or ticket.
- Prefer a dedicated least-privilege app role over `neondb_owner` when rotating.

| Date | Operator | Action |
|------|----------|--------|
| _pending_ | _pending_ | Rotation not yet performed |
