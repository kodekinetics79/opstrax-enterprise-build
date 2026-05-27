# OpsTrax Architecture

## Product Positioning

**OpsTrax Transport Management Solution** is designed as an enterprise-grade connected operations platform for fleet, dispatch, driver, asset, maintenance, safety, compliance, customer ETA, fuel, reporting, and AI-powered operational intelligence.

## Logical Architecture

```txt
React/Vite Frontend
    |
    | REST + WebSocket
    |
.NET Core API ---------------- MySQL
    |                            |
    | Core business modules       | Operational records
    |
Node.js Event Service -------- MySQL
    |
    | Telemetry, device events, safety events, AI workflows
    |
Future: GPS/OBD/Dashcam/Mobile App integrations
```

## Service Responsibilities

### Frontend

- Enterprise command center UI
- Module navigation
- Dashboard KPIs
- Live map placeholder
- AI insight panel
- Data tables for fleet, drivers, jobs, and maintenance

### .NET Core API

- Core business APIs
- Tenant-aware data access
- Vehicles, drivers, jobs, work orders, location events, dashboard summary, AI insights
- Future JWT/RBAC security layer

### Node.js Event Service

- Telemetry/event ingestion
- WebSocket broadcasting
- AI daily brief workflow
- Future hardware integration adapters
- Future queue/event stream support

### MySQL

- Multi-tenant operational data
- Seed data for demo
- Extensible schema for compliance, safety, maintenance, and AI insights

## Future Production Enhancements

1. Add authentication using JWT + refresh tokens.
2. Add proper tenant resolution from token claims.
3. Add Redis cache for live vehicle state.
4. Add queue/event bus such as RabbitMQ, Kafka, or Azure Service Bus.
5. Add object storage for photos, signatures, videos, documents.
6. Add React Native driver app.
7. Add Mapbox/Google Maps.
8. Add hardware adapters for GPS/OBD devices.
9. Add dedicated AI service with vector search and audit-safe prompt logging.
10. Add CI/CD with environment promotion.
