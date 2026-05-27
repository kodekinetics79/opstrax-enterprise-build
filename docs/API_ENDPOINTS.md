# OpsTrax API Endpoints

## .NET Core API

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /health | API health check |
| GET | /api/dashboard/summary | Executive dashboard summary |
| GET | /api/vehicles | Vehicle list |
| POST | /api/vehicles | Create vehicle |
| GET | /api/drivers | Driver list |
| POST | /api/drivers | Create driver |
| GET | /api/jobs | Jobs/orders list |
| GET | /api/maintenance/work-orders | Maintenance work orders |
| GET | /api/location-events/latest | Latest vehicle locations |
| GET | /api/ai/insights | AI insights |

## Node Event Service

| Method | Endpoint | Purpose |
|---|---|---|
| GET | /health | Node service health check |
| POST | /telemetry/location | Insert GPS/telemetry event |
| POST | /events/safety | Insert safety event |
| POST | /ai/generate-daily-brief | Generate AI daily brief placeholder |
| WS | /ws | Real-time WebSocket events |
