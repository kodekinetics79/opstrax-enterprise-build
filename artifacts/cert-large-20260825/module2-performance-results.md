# Module 2 browser performance results

All timings are observed in visible Google Chrome against the isolated certification tenant. They are not backend-only timings.

| Candidate | Role / scope | Journey | Dataset visible | Result | Pilot target | Outcome |
|---|---|---|---:|---:|---:|---|
| `d0e01c9…` | Dispatcher / CL-HQ | GPS Tracking cold visible settle through rendered page rows | 220 devices | 3,232 ms | <= 5,000 ms | Pass |
| `d0e01c9…` | Dispatcher / CL-HQ | Search `CLHQ-DEV-0138` through rendered 1-of-1 result | 220 devices | 1,626 ms | <= 2,000 ms | Pass |
| `a894229…` | Maintenance Manager / CL-HQ | OBD/J1939 cold navigation through initial rendered response | 200 diagnostics records | 3,934 ms | <= 5,000 ms | Pass |
| `a894229…` | Maintenance Manager / CL-HQ | OBD/J1939 post-deploy cold settle through table rows | 200 diagnostics records | 8,796 ms | <= 5,000 ms | Fail; Render Free cold/start transition |
| `a894229…` | Maintenance Manager / CL-HQ | Next-page transition | 200 diagnostics records / 50 per page | 2,500 ms observed wait; page 2 rendered | <= 3,000 ms | Pass |
| `a894229…` | Maintenance Manager / CL-HQ | Vehicle sort transition | 200 diagnostics records | 1,800 ms observed wait | <= 2,000 ms | Pass |
| `a894229…` | Maintenance Manager / CL-HQ | Exact vehicle search to 1-of-1 result | 200 diagnostics records | 2,220 ms | <= 2,000 ms | Fail by 220 ms |
| `4cdb6c4…` | Maintenance Manager / CL-HQ | Post-deploy identity discovery during full sign-out/sign-in | one role account | 23,559 ms resource duration | <= 5,000 ms | Fail; Render Free request/cold-runtime delay, login eventually passed |

Final-candidate full-tenant 1,100-device tables, 1,000-position Live Map soak, responsive viewports, authorized export, and repeated warm measurements remain required before the module verdict is closed. The two failed timing observations remain open performance findings rather than being hidden by successful API-only measurements.

## Supporting ingest performance

The signed-ingest harness is supporting evidence, not a browser result. R5 submitted 7,642 scenarios at a configured ceiling of eight submissions per second against API SHA `d0e01c9…`. Observed request latency was 1,576.5 ms p50, 1,746.4 ms p95, 2,770.9 ms p99, and 4,672.5 ms maximum. It recorded 7,641 passing expectations and one invalid time-dependent harness oracle; no credential or secret is included in this report. Evidence SHA-256: `cf9a6c0cf881b6d748b87e053fb3ba65cb67c98daab3be6ea2e9c3bc9f71b86e`.
