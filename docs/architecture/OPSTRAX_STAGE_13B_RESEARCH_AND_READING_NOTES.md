# Opstrax Stage 13B Research And Reading Notes

| Area | Files/Docs Read | Key Finding | Risk | Stage 13B Decision |
| --- | --- | --- | --- | --- |
| Safety foundation | `backend-dotnet/Services/SafetySchemaService.cs`, `backend-dotnet/Services/Batch4SchemaService.cs`, `backend-dotnet/Services/SafetyBackgroundService.cs` | Safety events, incidents, evidence packages, coaching tasks, and scorecards already exist and are durable. | Existing tables are bigint-based, not the explicit stage prompt wording. | Build a canonical summary layer on top of the live tables. |
| Maintenance foundation | `backend-dotnet/Services/Batch3SchemaService.cs`, `backend-dotnet/Services/MaintenanceSchemaService.cs`, `backend-dotnet/Services/MaintenanceBackgroundService.cs` | DVIR, defects, work orders, PM schedules, and availability logic already exist and are durable. | Legacy tables are split across batches. | Reuse them and add a fleet-health snapshot projection. |
| AI persistence | `backend-dotnet/Services/FoundationSchemaService.cs`, `backend-dotnet/Foundation/FoundationPersistenceServices.cs` | AI recommendations and action requests are already persisted and tenant-scoped. | Must remain recommendation-only for this stage. | Use the existing AI foundation for governed recommendations only. |
| Telemetry bridge | `backend-dotnet/Services/TelemetrySchemaService.cs`, `backend-dotnet/Services/TelemetryLiveStateService.cs` | Telemetry live state already carries correlation-safe fields and risk metadata. | Telemetry and safety/maintenance were not yet tied into a durable fleet-health snapshot. | Use telemetry as an input to the new snapshot projection. |
| API surface | `backend-dotnet/Controllers/EndpointMappings.cs` | Existing safety/maintenance/fleet-health APIs already return live operational data. | The platform still lacked a single foundation summary contract. | Add a guarded Stage 13B foundation summary endpoint. |

