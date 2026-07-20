# Stage 14B Verification and Fix Prompt

Use this next prompt only after Stage 14A cleanup is complete.

## Objective

Verify the main app cleanup and close the remaining live-service masking gap.

## Required checks

| Area | Check | Pass Criteria |
| --- | --- | --- |
| Dashboard naming | Confirm the visible shell still says `Dashboard`. | No visible Cockpit rename appears. |
| Route compatibility | Confirm `/command-center` still works. | Route remains intact. |
| Fuel API cleanup | Confirm seed imports are gone from the live fuel service. | No dead seed fallback residue. |
| Safety API cleanup | Confirm `create()` returns real backend failure instead of fabricating success. | No silent success mask. |
| Source regression | Add or extend tests for the above. | Test fails if fallback masking returns. |
| Build and lint | Re-run frontend and backend verification. | Clean results. |

## Next action

If all checks pass, Stage 14B should be a short verification-and-stabilization pass only. No mobile scope, no push, no deploy, and no production touch.

