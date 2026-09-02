---
name: opstrax-evidence-discipline
description: >
  Use when certifying a release, running UAT/regression gates, writing a defect
  register or test-results report, or about to call anything PASS, FIXED, or
  verified. Triggers: "is it fixed", "certify", "release decision", "GO/NO-GO",
  "gate", "test results", "defect register", "evidence", "retest", "UAT",
  "all tests pass", "ready to deploy", "sign off". ALSO use before trusting a
  green test that guards a large allowlist.
---

# OpsTrax Evidence Discipline

## What does NOT count as proof

- Source inspection, a successful compile, or a mocked test.
- A local-only pass. A pass against a contaminated environment.
- A green guard test whose allowlist or parser you have not audited.
- An empty result from agents that **failed** — that is not a clean review.

## Instruments before product

Three guard tests written during run RETEST-20260821-1035-R1 certified false
claims. Repair the measuring instrument BEFORE the code, or every subsequent
green is worthless. Known instrument failure modes in this repo:

| Instrument | How it lied |
|---|---|
| Runtime-schema parity test | scanned 2 of 48 schema services; matched column names **table-unqualified**, so pairs passed via collisions on unrelated tables |
| Endpoint gate-coverage test | `SkipString` treated an apostrophe in a **comment** as a string delimiter → 2.4MB method bodies inheriting gate markers from other methods; a comment counted as a gate |
| Migration orphan list | prose rationale ("re-established later") was simply false for six waves |
| Any owner-connection DB test | structurally blind to privilege defects — it must run as the restricted role |
| `npm run lint` | eslint ignores all of `frontend/src` |

**An allowlist with a prose justification is a liability.** Replace it with a
mechanical rule the test enforces (e.g. "no column referenced in controller SQL
may be allowlisted").

## Tests must fail before they pass

For any regression fix, verify **both directions**: the test fails against the
broken code and passes against the fix. A test only ever seen green may be
asserting nothing.

## Report counts honestly

Exact passed/failed/skipped/blocked. A missing database is **BLOCKED**, never
PASS. State the environment each lane ran against — a DB lane on a dev-shaped
schema proves handler logic only and cannot catch missing-column defects.

## Never fabricate a defect

Open a defect only from preserved evidence. If a condition is described but no
artifact records it, record it as **NOT CONFIRMED** and say why. Writing it up
anyway manufactures evidence.

## Retract loudly

If a PASS turns out to rest on a bad oracle, retract it explicitly in the
artifact and the ledger, state what the invalid evidence was, and re-run against
a valid one. A quiet correction is worse than the original error.

## Preserve prior-run evidence

Never modify a previous run's register, ledger, or screenshots. Write a **delta**
in the new run's folder. Preserve the user's uncommitted work — verify with
`git diff --stat` that files you do not own are untouched.

## Status vocabulary

FIXED / LIVE RETEST PASS · PARTIALLY FIXED · STILL FAILING · BLOCKED ·
NOT APPLICABLE TO DEPLOYED SHA.

Nothing is "LIVE RETEST PASS" until it passed **on the deployed SHA in the live
environment**. Code-complete work proven on an isolated environment is
REPAIRED (ISOLATED PROOF) — a different and lesser claim.

## Know which SHA a defect applies to

Compare deployed SHA vs HEAD before repairing anything. Files absent at the
deployed SHA make a defect NOT APPLICABLE TO DEPLOYED SHA — a pre-ship blocker,
not a live incident. `git cat-file -e <sha>:<path>` settles it in one command.
