# OpsTrax Performance Budgets

## Budget Areas
- Live Map load
- Command Center load
- Customer 360 load
- Revenue Cockpit load
- Fleet Health load
- AI recommendation generation
- IoT ingestion
- Alert processing
- Dashboard summary queries

## Working Targets
- Common dashboard reads should feel interactive and not stall the operator.
- Heavy summaries should use pre-aggregated or materialized data where practical.
- IoT ingestion and alerting must be partition-aware and non-blocking.

