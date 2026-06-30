# Stage 9 POD and Site Access Architecture

## Purpose

This slice gives the backend a real operational proof foundation without building the full customer, contract, or invoice stack yet.

## Flow

1. An operator or dispatcher records a site access requirement.
2. The backend persists the requirement with tenant scope, timestamps, and optional metadata.
3. If the requirement is tied to a job, the backend raises a safe AI recommendation to flag operational risk.
4. Access documents can be created and updated separately from the requirement itself.
5. A waiver path can request approval when the document is too risky to auto-complete.
6. Proof packages can only move forward when evidence exists, or when an explicit exception note is supplied.

## Safety rules

- Do not write business tables directly from AI.
- Do not bypass tenant scope.
- Do not auto-complete a waiver or proof submission that needs approval or evidence.
- Preserve correlation and causation ids for operational traceability.

## Mobile value

- The model is replay-safe for weak network submits.
- The records carry enough metadata for a future driver / operator app.
- Evidence can be audited without pretending the UI is a desktop-only admin screen.

