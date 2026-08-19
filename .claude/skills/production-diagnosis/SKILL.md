---
name: production-diagnosis
description: >
  Use when something works locally but fails on a deployed system, when an endpoint
  returns 500, or when diagnosing anything on live infrastructure. Triggers: "it's
  broken in production", "500 error", "works locally", "why is it failing live", "check
  the logs", "unable to load", "is it deployed", "the page is empty", "schema drift",
  "migration", "why did that not apply", "permission denied", "it hangs", "lock timeout".
  Also use before writing any DDL against a live database.
---

# Production diagnosis

The discipline that separates a two-hour fix from a two-day one: **stop guessing, and
make the running system tell you.** Every hour lost is usually an hour spent reasoning
about code while the answer sat in a log line.

## Work outside-in, never inside-out

The instinct is to open the source and reason about what might be wrong. Resist it. Start
at the edge and move inward, because each step localises the fault for free:

1. **Health/readiness endpoint.** Most systems expose their own self-assessment. Read it
   first; it often names the failing subsystem outright.
2. **Hit the endpoints and record status codes.** A table of `200/500` per endpoint is
   worth more than any amount of code reading. Two endpoints reading different tables —
   one green, one red — localises the fault instantly.
3. **Read the actual error from the deployment's logs.** Do not infer it.
4. **Only now** open the code, with the exact error in hand.

The single highest-leverage move is step 3. A generic `{"message":"Internal server
error"}` in the HTTP response is not the error; the process logs carry the real one, with
its error code and position.

```bash
# Render, as an example. Every platform has an equivalent — find it before you need it.
render logs --resources <srv-id> --limit 40 --level error --output text --confirm
```

Map the codes; each points somewhere specific and different:

| Code | Meaning | Where the fix lives |
|---|---|---|
| `42703` | undefined column | schema behind code |
| `42P01` | undefined table | schema behind code |
| `42501` | permission denied / RLS violation | grants, or missing tenant context |
| `23502` | not-null violation | code writing NULL into a legacy constraint |
| `23505` | unique violation | idempotency / replay |
| `40001` | serialization failure | transaction design |

`42501` deserves special attention: it is *two* different bugs wearing one code — a
missing GRANT, or code running without the session context an RLS policy requires. Check
`relacl` and the policies before assuming either.

## Verify what is actually deployed

Compare the running build against your branch before theorising. Most deployments expose
a version or commit in a health endpoint or every log line.

```bash
git merge-base --is-ancestor <deployed-sha> main && git rev-list --count <deployed-sha>..main
```

"The deployed code is newer than its database" explains an enormous class of production-only
failures, and is invisible from the source tree alone.

## Migration ledgers lie

A `schema_migrations` table records what someone *ran*, not what the schema *is*. Tables
appear without their migration recorded; migrations record without fully applying.

**Inspect the schema directly** — `information_schema.columns`, `to_regclass`,
`pg_class.relacl`, `pg_policies`. Direct inspection is authoritative; the ledger is a
hint. A ledger once showed 27 unapplied migrations while several of their tables already
existed, and the truth was that exactly two tables and a handful of columns were missing.

Corollary: **fix the specific gap, not the whole backlog.** Running 27 accumulated
migrations to repair two missing tables — days before a demo, including unrelated
finance and auth changes and a security cutover that rewrites policies — is far more
dangerous than the targeted repair.

## Runtime-created schema is a second, invisible class of drift

Some systems create schema at boot from code (`EnsureColumn`-style services) rather than
from migrations. When production runs under a restricted role, that startup path is often
skipped — so those objects exist in every developer's database and in **no** production.

They will never appear in your migration diff. When a column is missing and no migration
owns it, grep the application's schema services before concluding the code is wrong.

## DDL against a live database

Adding a column looks free. It is not.

- `ALTER TABLE` takes **ACCESS EXCLUSIVE**, and a *queued* exclusive request blocks every
  reader arriving behind it. On a hot table that is a self-inflicted outage.
- Long-running application transactions (background workers, connection leaks, anything
  `idle in transaction`) hold their locks until commit, so your ALTER may never win.

Therefore:

```sql
SET lock_timeout = '3s';   -- fail fast, never queue in front of readers
```

and **no enclosing transaction** for multi-statement column adds, so one blocked
statement neither rolls back its siblings nor holds their locks while waiting. Then
**retry** rather than waiting longer. Raising the timeout is the wrong instinct — it
converts a harmless failure into an outage.

Two subtleties that cost real time:

- **`ADD COLUMN IF NOT EXISTS` still takes the lock** even when the column exists. The
  `IF NOT EXISTS` suppresses the *error*, not the *lock*. A retry loop over already-applied
  statements will spin forever fighting for locks to do nothing. **Pre-check each
  statement** with a cheap read and skip it.
- **Column-level grants do not extend to new columns.** If a table grants SELECT
  column-by-column (common where some columns hold secrets), every column you add lands
  unreadable, and the endpoint starts failing `42501` — a scarier error than the missing
  column you just fixed. Check `relacl` before altering; recompute grants after.

Before diagnosing a hang, look:

```sql
SELECT pid, state, now()-xact_start AS xact_age, wait_event_type, left(query,60)
FROM pg_stat_activity WHERE state <> 'idle' ORDER BY xact_start;
```

## Verify the fix against the running system

A green build is not a fix. Re-run the same probe that showed the failure and put the
before/after side by side. "The 503 is gone, confirmed by probe" is worth more than a
paragraph of reasoning about why it should be.

And when you fix one error, **expect the next one to surface**. A handler that fails on
the first missing column cannot tell you about the second. Iterate: fix, re-probe, read
the new error. Three rounds is normal and is not a sign of poor diagnosis.

## Report honestly

State what you verified and what you assumed. If you introduced a regression while
fixing — a grant you forgot to recompute, a counter you moved the wrong way — say so
plainly and fix it. Silent self-correction erodes trust faster than the original bug.
