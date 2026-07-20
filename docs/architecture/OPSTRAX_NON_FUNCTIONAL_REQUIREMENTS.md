# OpsTrax Non-Functional Requirements

## Targets
- API p95 latency: sub-second for common reads
- Dashboard initial render: fast enough for executive use without stalling
- IoT ingestion: high-volume, partition-ready
- Availability: production-ready high availability target, local demo may be lower
- RTO/RPO: explicitly defined before production launch
- AI latency: recommendations should be timely but not block core workflows
- Observability: logs, metrics, traces, and audit events
- Cost control: store raw events economically and roll up heavy dashboards

## Assumptions
- PostgreSQL is the authoritative datastore.
- The current run is local-only and must avoid production assumptions.

