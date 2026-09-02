---
name: honest-interfaces
description: >
  Use when building or reviewing any UI that displays data a system might not have —
  dashboards, scorecards, KPI tiles, health scores, maps, status badges, analytics.
  Triggers: "add a KPI", "dashboard", "score", "health", "default value", "fallback",
  "empty state", "the page looks empty", "make it look better", "placeholder data",
  "seed data", "demo data", "why is this showing 0", "N/A", or any component that
  renders a number, status, or assessment derived from an API response. Also use before
  writing `?? 0`, `?? 100`, `|| "Good"`, or any default for a value meant as a
  measurement.
---

# Honest interfaces

A number on screen is a claim. If the system cannot substantiate the claim, the screen
must not make it. This is not pedantry — fabricated values are the fastest way to destroy
trust with a technical buyer, and they are almost always introduced casually, by a
one-character default.

## The core rule

**Absence must render as absence.** Never let a fallback masquerade as a measurement.

```tsx
// WRONG — a tenant with no computed health renders a confident "50% / Action required"
const score = num(summary.fleetHealthScore, 50);
const avgSafety = num(summary.avgSafetyScore, 100);

// RIGHT — null means null, and the UI says so
const score = optional(summary.fleetHealthScore);   // number | null
{score == null ? "—" : `${score}%`}
{score == null ? "Not yet measured" : verdict(score)}
```

The tell: a default that is *plausible*. `?? 0` is usually honest (a count really is
zero). `?? 50` and `?? 100` are not — nobody defaults to a value unless they want the
gauge to look populated. **A plausible default is a lie with better manners.**

## Taxonomy — three things that look identical on screen

Distinguish them in the data model, not just visually:

| Meaning | Renders as | Never |
|---|---|---|
| Measured zero | `0` | `—` |
| Not measured / no data yet | `—`, "Not yet measured" | a number |
| Measurement failed | an error state | silence, or a stale value |

Collapsing "no data" into "zero" understates. Collapsing it into a mid-range default
overstates. Both are wrong; the second is worse because it is undetectable.

## Provenance: say where data came from

When a surface can show data of mixed origin — live device, seeded, simulated, imported,
manually entered — **label it in the UI**. A provenance badge is not clutter; it is the
feature that lets you demo seeded data honestly.

The rule that follows: seeded or simulated records must be *stamped at write time* with a
source the UI can classify. If synthetic data enters through the same path as real data
and carries the same provenance, no interface can tell the truth afterwards — the
honesty has to exist in the database, not the component.

## Unknown is not failure

A missing input is not the same as a bad reading. Watch for code that treats them alike:

```tsx
// A live-state query supplied no device/camera columns, so callers passed "--".
// "--" is truthy, matched no health regex, and every marker on a healthy fleet
// rendered RED — the app reported total failure because it lacked information.
if (!deviceOnline && !cameraOnline) return RED;
```

Gate only on channels that actually reported. If nothing reported, fall through to a
neutral state, not an alarming one. **Ask: does this red mean "broken", or "I don't
know"?** Users cannot tell, so the code must.

## Client-side derivation is fine; client-side invention is not

Deriving a recommendation from real fields is legitimate, and worth doing:

- ✅ thresholds over live values (`idleMinutes >= 45 && readiness >= 75`), especially when
  gated on evidence (`utilization_basis` starting with `trip_hours_30d`)
- ✅ text clearly labelled **"Rule-Based Recommendation"**
- ❌ prose implying analysis that never ran ("AI detected…", "Our model predicts…")
- ❌ template strings presented as computed findings

The test is whether a reader could mistake the mechanism. Label the mechanism and you can
be as helpful as you like.

## Empty states are a feature

A page with nothing in it should say *what* is absent and *why*, and offer the next
action. "No current GPS positions — no device has reported in the last 15 minutes" is
strictly better than a blank panel, and infinitely better than fake rows. Teams reach for
placeholder data precisely because empty states were never designed; design them and the
temptation disappears.

## Review checklist

Before shipping any data-bearing component:

1. Grep the diff for `?? `, `|| `, `: 0`, `: 100`, `"Good"`, `"Healthy"` on display paths.
2. For each: if the API omits this, does the screen make a claim? If yes, it is a bug.
3. Does every KPI key the component reads actually exist in the endpoint's response?
   (Four tiles once read keys no endpoint emitted and showed `--` forever — nobody
   noticed, because `--` looked deliberate.)
4. Can a viewer distinguish real / seeded / simulated?
5. Is any status colour saying "broken" when it means "unknown"?
6. Are field names in the client's type the same case the API actually sends? A
   snake_case declaration against a camel-casing serializer reads `undefined` silently
   and renders as an em dash that looks intentional.

Point 6 is the recurring one: silent shape mismatches never throw. They just quietly
render nothing, forever, and everyone assumes the data is missing upstream.
