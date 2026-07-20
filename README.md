# OpsTrax — Transport Management Solution

**OpsTrax** is an enterprise-grade, connected operations platform for fleets, transport teams, drivers, vehicles, assets, dispatch, maintenance, safety, compliance, cost intelligence, and AI-powered decision support.

> Developed by **Kode Kinetics** · [www.kodekinetics.com](https://www.kodekinetics.com) · info@kodekinetics.com · +1 571 430 5333

---

## Platform Summary

| Attribute | Value |
|---|---|
| Product | OpsTrax Transport Management Solution |
| Developer | Kode Kinetics |
| Version | Enterprise Demo Build |
| Environment | Local / Demo |
| Frontend port | **10000** |
| API port | **8088** |
| Node Events port | **8090** |
| Database | MySQL 8.4 (internal only — not exposed) |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React 19.2 · TypeScript · Vite · Tailwind CSS v4 · TanStack Query v5 · React Router v6 |
| Backend API | ASP.NET Core 8 Minimal API · C# 12 · MySqlConnector |
| Node Events | Node.js 20 · Express · ws (WebSocket) |
| Database | MySQL 8.4 |
| Container | Docker Compose |
| Reverse Proxy | Nginx (production build) |

---

## Enterprise Modules (37)

| Group | Modules |
|---|---|
| Command | Command Center · Live Control Tower |
| Dispatch | Dispatch Board · Jobs & Orders · Route Planning · Customer ETA Portal · Carrier Management |
| Fleet | Vehicles · Drivers · Assets / Trailers / Equipment |
| Maintenance | Maintenance · Work Orders |
| Safety | Safety · AI Dashcam / Incident Review · Driver Coaching · Incidents · Evidence Packages |
| Compliance | Compliance · HOS / ELD Framework · DVIR / Inspections |
| Finance | Fuel & Idling · Expenses · Contracts / Rates |
| Intelligence | Reports & Analytics · SLA / KPI Center · Predictive Cost & Margin · ROI / Cost Leakage · Audit Logs · AI Copilot |
| Platform | Integrations · User Management · Settings · Billing · Companies / Tenants · White Label / Reseller · About |

---

## Pilot Credentials

Credentials are environment-managed and are never published in the repository. Platform
bootstrap credentials use `PLATFORM_SUPERADMIN_EMAIL` and
`PLATFORM_SUPERADMIN_PASSWORD`. Optional demo seeding requires
`DemoSeed__Enabled=true` and `DemoSeed__Password`; keep demo seeding disabled in
production. Tenant and portal users must be provisioned through the admin workflows.

---

## Quick Start

```bash
# Start all services
docker compose up --build

# Frontend
http://localhost:10000

# API
http://localhost:8088

# Node Events (WebSocket)
http://localhost:8090
```

---

## Country & Compliance Coverage

OpsTrax includes localization and compliance framework support for:

| Country | Currency | HOS/ELD Framework | Language |
|---|---|---|---|
| United States | USD | FMCSA HOS rules (US) | English (en-US) |
| Canada | CAD | Transport Canada HOS | English / French (fr-CA) |
| Saudi Arabia | SAR | SFDA/MOT transport rules | Arabic (ar-SA) |
| UAE | AED | RTA / TDRA transport rules | Arabic (ar-AE) |
| Pakistan | PKR | NTRC transport rules | Urdu (ur-PK) |

---

## Supported Languages

| Language | Code | Direction |
|---|---|---|
| English (US) | en-US | LTR |
| French (Canada) | fr-CA | LTR |
| Arabic (Saudi Arabia) | ar-SA | RTL |
| Arabic (UAE) | ar-AE | RTL |
| Urdu (Pakistan) | ur-PK | RTL |

---

## Compliance Disclaimer

> **OpsTrax provides compliance management, monitoring, and audit-readiness tools. Final regulatory compliance remains the carrier's / operator's responsibility. ELD certification and regulatory approval depend on the connected ELD device/provider and applicable country requirements. OpsTrax is not a certified ELD and does not claim certification by FMCSA, Transport Canada, or any other regulatory authority.**

---

## Architecture

```
┌─────────────────────────────────────────┐
│  Frontend (React / Vite)  :10000        │
│  Nginx reverse proxy                    │
└────────────┬────────────────────────────┘
             │ HTTP REST
┌────────────▼────────────────────────────┐
│  ASP.NET Core 8 API       :8088         │
│  - 200+ REST endpoints                  │
│  - JWT-style session tokens             │
│  - RBAC role enforcement                │
└────────────┬────────────────────────────┘
             │ MySQL
┌────────────▼────────────────────────────┐
│  MySQL 8.4               (internal)     │
│  - Auto-migrating schema                │
│  - Pre-seeded demo data                 │
└─────────────────────────────────────────┘
┌─────────────────────────────────────────┐
│  Node Events (WebSocket)  :8090         │
│  - Real-time fleet events               │
│  - Broadcast to connected clients       │
└─────────────────────────────────────────┘
```

---

## About Kode Kinetics

Kode Kinetics is a technology company specializing in:

- Custom SaaS Platforms
- AI Automation & AI Copilot Systems
- Web & Mobile Applications
- ERP / CRM Integrations
- Cloud Solutions
- Data Dashboards & Reporting
- Workflow Automation
- Enterprise System Architecture
- Public-Sector / Compliance-Ready Systems
- API Development & Integration

**Contact:** [www.kodekinetics.com](https://www.kodekinetics.com) · info@kodekinetics.com · +1 571 430 5333

---

*Connected transport. Intelligent control. Enterprise execution.*
