# Stage 9 AI Operations Liaison

## Role of AI in this slice

AI is used as an operational liaison, not an executor.

## Allowed behavior

- Create recommendations when evidence is missing.
- Raise attention when site access is unresolved.
- Store reasoning / recommendation intent in the database.
- Request approval for high-risk actions.

## Disallowed behavior

- Direct writes into customer, contract, job, trip, revenue, or finance business tables.
- Silent execution of smart assignment or waiver actions.
- External provider calls that are required to make the local runtime work.

## Operational interpretation

- AI supports the human workflow.
- The human workflow still owns the final business effect.
- Correlation and causation ids should make each recommendation traceable later.

