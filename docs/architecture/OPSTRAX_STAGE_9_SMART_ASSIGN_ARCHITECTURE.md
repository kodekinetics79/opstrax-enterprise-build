# Stage 9 Smart Assign Architecture

## Purpose

Stage 9 turns smart assignment into a governed operational recommendation flow instead of a loose UI helper.

## Flow

1. The backend creates a recommendation for a job and optional trip.
2. The recommendation stores score, confidence, risk, and proposed action data.
3. If the recommendation is risky, accepting it creates an approval request.
4. If the recommendation is safe enough, the system creates an assignment confirmation record.
5. Recommendation accept/reject activity is published through the existing durable event path.

## Safety rules

- Accepting a high-risk recommendation must not silently perform the reassign action.
- Cross-tenant access is denied by design.
- Recommendation creation should be replay-safe when an idempotency key is supplied.

## Why this matters

- It gives operations a structured way to suggest driver / vehicle / crew alignment.
- It creates an auditable paper trail before the first business spine slice lands.
- It keeps the AI layer advisory rather than autonomous.

