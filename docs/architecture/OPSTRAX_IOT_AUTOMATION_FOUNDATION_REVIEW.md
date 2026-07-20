# OpsTrax IoT Automation Foundation Review

| Area | Current Support | Missing Pieces | Risk | Required Foundation | Priority | P0/P1/P2 |
|---|---|---|---|---|---|---|
| Device registry | Device routes and seed data exist | No formal device lifecycle model across all sources | Device state may be inconsistent | Add canonical device registry and history | P0 |
| Assignment history | Some vehicle/device links exist | No explicit assignment-history contract | Hard to audit which device tracked which vehicle | Record assignment history | P0 |
| Ingestion | Telemetry ingestion endpoint exists | In-memory/demo path still appears in Node service | Not durable enough for enterprise | Normalize into backend persistence | P0 |
| Idempotency | Nonce table exists for telemetry | Not broad enough for all event types | Duplicate actions may occur | Extend idempotency to all event/action flows | P0 |
| Signature/replay | HMAC and nonce checks exist on backend telemetry path | Not universal across integrations | Spoofing/replay risk | Standardize signed inbound checks | P0 |
| Domain events | Telemetry alerts are produced | No durable event bus | AI and risk engine cannot reliably subscribe | Add event backbone | P0 |
| Risk scoring | Alerts and device health exist | No unified sensor-to-risk pipeline | Important signals may be siloed | Normalize telemetry into risk signals | P0 |
| AI trigger flow | Some recommendations exist | No formal AI trigger chain from telemetry | Automation remains ad hoc | Connect ingestion -> risk -> AI -> action | P1 |
| Command safety | Browser/UI can draft actions | No strict command policy layer | Physical commands could be unsafe | Gate lock/unlock/immobilize behind approval | P0 |
| Retention/partitioning | Tables exist with timestamps | No formal retention/partition strategy | High-volume tables may degrade | Define retention and partition-ready tables | P1 |

