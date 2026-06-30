# Compliance / Documents / Expiry / Permits / Insurance

## Current State
- `CompliancePage.tsx` already presents multi-country compliance, violations, driver status, vehicle status, audit packages, and AI advice.
- Related fleet compliance surfaces remain linked through the main shell and module config.

## Productization Notes
- Compliance views should continue to prefer live data and show explicit unavailable states when backend reads fail.
- AI recommendations must stay assistive only.

## Remaining Gaps
- Some tabs still collapse backend errors into a generic unavailable state.
- Documents, permits, and insurance remain scattered across more than one operational surface.

## Verdict
- Strong enough for Stage 15A-2, with low-to-medium polish risk.
