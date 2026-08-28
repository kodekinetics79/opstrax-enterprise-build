# Module 2 browser performance results

All timings are observed in visible Google Chrome against the isolated certification tenant. They are not backend-only timings.

| Candidate | Role / scope | Journey | Dataset visible | Result | Pilot target | Outcome |
|---|---|---|---:|---:|---:|---|
| `d0e01c9…` | Dispatcher / CL-HQ | GPS Tracking cold visible settle through rendered page rows | 220 devices | 3,232 ms | <= 5,000 ms | Pass |
| `d0e01c9…` | Dispatcher / CL-HQ | Search `CLHQ-DEV-0138` through rendered 1-of-1 result | 220 devices | 1,626 ms | <= 2,000 ms | Pass |

Final-candidate full-tenant 1,100-device tables, 1,000-position Live Map soak, responsive viewports, export, and repeated refresh measurements remain required before the module verdict is closed.

## Supporting ingest performance

The signed-ingest harness is supporting evidence, not a browser result. R5 submitted 7,642 scenarios at a configured ceiling of eight submissions per second against API SHA `d0e01c9…`. Observed request latency was 1,576.5 ms p50, 1,746.4 ms p95, 2,770.9 ms p99, and 4,672.5 ms maximum. It recorded 7,641 passing expectations and one invalid time-dependent harness oracle; no credential or secret is included in this report. Evidence SHA-256: `cf9a6c0cf881b6d748b87e053fb3ba65cb67c98daab3be6ea2e9c3bc9f71b86e`.
