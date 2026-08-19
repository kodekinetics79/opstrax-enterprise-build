---
name: demo-readiness
description: >
  Use when preparing a customer demo, pilot, or evaluation environment, or when deciding
  what data to put in front of a buyer. Triggers: "demo", "pilot", "show the customer",
  "prepare for the meeting", "make it look good", "seed the tenant", "test data",
  "the page is empty", "will this impress", "sales deck", "POC", "evaluation", or any
  request to populate an environment someone outside the team will see.
---

# Demo readiness

A demo fails in one of two ways: it looks empty, or it looks fake. Teams fear the first
and cause the second. **Empty is recoverable in an afternoon; fake destroys the deal and
the relationship**, because the buyer's engineer will find it and will then doubt
everything else you showed.

## The buyer's engineer is in the room

Assume someone technical is watching, and that they have seen the incumbent product.
They check, roughly in this order, within the first two minutes:

1. **Geography.** Do vehicles/assets sit where this customer actually operates? Do they
   follow roads, or cut across water and farmland?
2. **Physics.** Does the stated speed match the distance covered on the scale bar? Does
   anything ever stop?
3. **Timestamps.** Is everything updated at a suspiciously regular cadence, or aligned to
   the second? Is the "last updated" older than the page claims?
4. **Uniformity.** Is every record healthy, every score round, every row identical in
   shape?
5. **Cross-page agreement.** Does the same fleet report different numbers on two screens?

Each is cheap to get right and fatal to get wrong.

## Coherence rules

**Match the tenant's real profile before generating anything.** Read its country,
currency, timezone and stated service area first. Seeding Virginia coordinates into a
Toronto tenant — or Riyadh coordinates into a US one — is visible in seconds at street
zoom, and it is a self-inflicted wound. The same applies to currency symbols, units
(mph vs km/h), phone formats, address formats and compliance regimes (US HOS clocks
beside a non-US flag will be noticed by a compliance buyer immediately).

**Match existing vocabularies.** Query `SELECT DISTINCT` on status columns before
inventing values. Systems commonly reject or mis-render statuses that look obvious but
are not in use (`On Track`, not `On Time`; no `Return` where only `Drop-off` exists).
Invented enums either fail on insert or render as unstyled fallbacks.

**Make timestamps relative to now.** Data anchored to fixed dates is stale the next
morning and often crosses a freshness threshold that flips the whole board to
"Offline". Seed relative to `NOW()`, and make the seed re-runnable so it can be refreshed
minutes before the meeting.

## Deliberate imperfection

A board where everything is green reads as fake, because real operations are never
uniformly healthy. Build in:

- one item genuinely at risk, one delayed, one completed
- one asset **offline** — and let it be honestly offline (an old timestamp), not faked
  with a flag. Pointing at it first is a **trust move**: "we don't hide these."
- variation in scores, fuel levels, behaviour profiles; no round numbers everywhere
- irregular update intervals, sub-second precision on timestamps

Counter-intuitively, showing an offline unit and an at-risk job makes the rest of the
demo *more* believable, not less.

## Seeded data is fine. Disguised data is not.

You may absolutely demo with synthetic data. The requirement is that the system can tell
the truth about it:

- stamp synthetic records with a source the UI classifies and badges (SEED / SIM)
- never write synthetic data through the path reserved for real hardware or partners —
  once it carries the same provenance as genuine data, no interface can distinguish it
  afterwards, and you have permanently polluted the audit story
- be alert to side effects: a synthetic "device fix" may flip a device to *Active* and
  write a *commissioning record*. That is fabricated field history in a real tenant.

Use a **dedicated demo tenant**. Never seed a tenant holding real customer data.

## Sequence the work

1. **Make it correct** — endpoints return 200, pages render, no fabricated defaults.
2. **Make it coherent** — right geography, vocabulary, currency, timestamps.
3. **Make it populated** — enough volume to look like an operation, not a test fixture.
4. **Make it move** — live behaviour, if the product's claim is real-time.
5. **Rehearse the click path** end to end, on the tenant you will actually use.

Most teams start at 3 and never reach 1. An empty page on a correct system is a far
better position than a full page on a broken one, because the first is an afternoon of
seeding and the second is unbounded.

## Know what you cannot show

Enumerate honestly, in advance, what the product cannot do yet, and decide what you will
say when asked. A crisp "not yet — that's on the roadmap for Q3, here's the design"
costs nothing. A demo that gestures at a capability which does not exist ends the
evaluation the moment someone asks to click it.

Then, before the meeting: **walk every path you plan to show, in the tenant you will
show it in, with the account you will use.** Most demo failures are not missing features;
they are a permission gap, an empty tenant, or a stale login discovered live.

## The scale question

Someone will ask how it performs at 10× the demo fleet. Know the real answer — writes
per fix, rows per hour, what is partitioned and what is not, what has been load-tested
versus reasoned about. "Here's what we've measured, here's what's designed but not yet
wired" is a credible answer. A confident guess that collapses under one follow-up is not.
