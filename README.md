# OpsTrax Transport Management Solution

Connected transport. Intelligent control. Enterprise execution.

OpsTrax is a connected enterprise transport command center with fleet, dispatch, safety, maintenance, compliance, customer ETA, finance, analytics, audit, integrations, billing, and local AI copilot workflows.

## Local Run

```bash
cp .env.example .env
chmod +x start-local.sh stop-local.sh reset-local.sh
./start-local.sh
```

Open:

- Frontend: http://localhost:10000
- Swagger: http://localhost:8088/swagger
- Node Events: http://localhost:8090/health

MySQL is internal Docker network only. It is exposed to other containers on `3306` and is not mapped to a host port.

## Demo Credentials

All users use password `Admin@12345`.

- admin@opstrax.com
- dispatcher@opstrax.com
- driver@opstrax.com
- mechanic@opstrax.com
- customer@opstrax.com

## Structure

```text
frontend/             React + Vite + TypeScript + Tailwind + TanStack Query
backend-dotnet/       ASP.NET Core .NET 8 API + Swagger + MySqlConnector
services/node-events/ Node Express SSE live event service
database/init/        MySQL schema and seed data
docker-compose.yml    Full local stack
```

## Ports

```text
Frontend: 10000 -> container 80
API:      8088  -> container 8080
Node:     8090  -> container 8090
MySQL:    internal only
```
