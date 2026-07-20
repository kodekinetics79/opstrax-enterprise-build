# Project Overview

<cite>
**Referenced Files in This Document**
- [README.md](file://README.md)
- [ARCHITECTURE.md](file://docs/ARCHITECTURE.md)
- [PRODUCT_MODULES.md](file://docs/PRODUCT_MODULES.md)
- [API_ENDPOINTS.md](file://docs/API_ENDPOINTS.md)
- [docker-compose.yml](file://docker-compose.yml)
- [Program.cs](file://backend-dotnet/Program.cs)
- [app.ts](file://backend/src/app.ts)
- [compliance.registry.ts](file://backend/src/modules/compliance/compliance.registry.ts)
- [device.registry.ts](file://backend/src/modules/devices/device.registry.ts)
- [moduleConfig.ts](file://frontend/src/modules/moduleConfig.ts)
- [I18nProvider.tsx](file://frontend/src/i18n/I18nProvider.tsx)
- [AiCopilotPage.tsx](file://frontend/src/pages/AiCopilotPage.tsx)
- [rbacConfig.ts](file://frontend/src/auth/rbacConfig.ts)
- [001_schema.sql](file://database/init/001_schema.sql)
</cite>

## Table of Contents
1. [Introduction](#introduction)
2. [Project Structure](#project-structure)
3. [Core Components](#core-components)
4. [Architecture Overview](#architecture-overview)
5. [Detailed Component Analysis](#detailed-component-analysis)
6. [Dependency Analysis](#dependency-analysis)
7. [Performance Considerations](#performance-considerations)
8. [Troubleshooting Guide](#troubleshooting-guide)
9. [Conclusion](#conclusion)
10. [Appendices](#appendices)

## Introduction
OpsTrax is an enterprise-grade, connected operations platform designed to unify fleet, dispatch, driver, asset, maintenance, safety, compliance, cost intelligence, and AI-powered decision support. It provides a comprehensive command center for modern transport organizations, enabling real-time visibility, intelligent insights, and auditable workflows across borders and languages.

Key platform goals:
- Connected operations: centralized control and live telemetry
- Regulatory readiness: modular compliance packs for multiple jurisdictions
- Multilingual and localized experiences: regional languages and formats
- AI copilot: natural language query interface for operations
- Enterprise-grade security and scalability: containerized microservices architecture

**Section sources**
- [README.md:1-166](file://README.md#L1-L166)

## Project Structure
The repository is organized into layered services:
- Frontend (React 19.2 + TypeScript + Vite + Tailwind)
- Backend (.NET 8 minimal API in C#)
- Node.js event service (WebSocket streaming and telemetry ingestion)
- Database (PostgreSQL schema with tenant-aware tables)
- Docker orchestration (compose for local dev/demo)

```mermaid
graph TB
subgraph "Frontend"
FE["React 19.2<br/>Vite + Tailwind + TanStack Query"]
end
subgraph "Backend"
API[".NET 8 Minimal API<br/>C# 12 + MySQL Connector"]
EVT["Node.js Event Service<br/>WebSocket + Ingestion"]
end
DB["PostgreSQL Schema<br/>Tenant-aware tables"]
FE --> API
FE --> EVT
API --> DB
EVT --> DB
```

**Diagram sources**
- [docker-compose.yml:1-45](file://docker-compose.yml#L1-L45)
- [README.md:24-34](file://README.md#L24-L34)

**Section sources**
- [README.md:24-34](file://README.md#L24-L34)
- [docker-compose.yml:1-45](file://docker-compose.yml#L1-L45)

## Core Components
- Command Center and Control Tower: consolidated dashboards for live operations
- Dispatch and Routing: jobs, orders, route plans, and dispatch boards
- Fleet Management: vehicles, drivers, assets, and assignments
- Telematics & IoT: GPS, OBD/J1939, sensors, dashcams, and device health
- Safety & Compliance: incidents, DVIR, HOS/ELD, driver coaching, and evidence packages
- Maintenance: work orders, preventive schedules, and downtime tracking
- Finance: fuel/idling, expenses, invoices, payments, and profitability
- Intelligence: reports/analytics, predictive insights, carbon tracking, and AI Copilot
- Platform: integrations, user/role management, audit logs, settings, and governance

**Section sources**
- [README.md:37-49](file://README.md#L37-L49)
- [PRODUCT_MODULES.md:1-66](file://docs/PRODUCT_MODULES.md#L1-L66)

## Architecture Overview
OpsTrax employs a layered, tenant-aware architecture with clear separation of concerns:
- Frontend: enterprise command center UI with module navigation, dashboards, and AI insights
- Backend API: REST endpoints for core business domains, tenant-aware data access, and RBAC
- Node Events: real-time telemetry and event broadcasting via WebSocket
- Database: multi-tenant schema supporting fleet, safety, compliance, and AI insights

```mermaid
graph TB
subgraph "Client"
UI["React Frontend"]
end
subgraph "API Layer"
RAPI["REST API (.NET 8)"]
AUTH["RBAC + Session Validation"]
end
subgraph "Streaming"
WS["Node Events WebSocket"]
end
subgraph "Persistence"
PG["PostgreSQL"]
end
UI --> RAPI
UI --> WS
RAPI --> AUTH
AUTH --> PG
WS --> PG
```

**Diagram sources**
- [ARCHITECTURE.md:1-69](file://docs/ARCHITECTURE.md#L1-L69)
- [README.md:117-142](file://README.md#L117-L142)

**Section sources**
- [ARCHITECTURE.md:1-69](file://docs/ARCHITECTURE.md#L1-L69)
- [README.md:117-142](file://README.md#L117-L142)

## Detailed Component Analysis

### Enterprise Modules and Capabilities
The platform organizes functionality into 37+ modules grouped by domain:
- Command and Control: Command Center, Live Control Tower
- Dispatch: Dispatch Board, Jobs & Orders, Route Planning, Customer ETA Portal, Carrier Management
- Fleet: Vehicles, Drivers, Assets, Maintenance, Work Orders, Inspections
- Safety and Compliance: Safety, AI Dashcam, Driver Coaching, Incidents, Evidence, DVIR-ready, HOS/ELD
- Finance: Fuel & Idling, Expenses, Contracts/Rates
- Intelligence: Reports & Analytics, SLA/KPI Center, Predictive Cost/Margin, ROI/Cost Leakage, Audit Logs, AI Copilot
- Platform: Integrations, User Management, Settings, Billing, Companies/Tenants, White Label/Reseller, About

Practical examples:
- Dispatch operators use the Dispatch Board to match jobs to drivers and vehicles, monitor exceptions, and adjust route plans.
- Safety managers review dashcam events, manage incidents, and track driver coaching outcomes.
- Finance teams analyze fuel transactions, idling costs, and profitability while generating SLA/KPI reports.
- Fleet managers monitor vehicle readiness, driver HOS posture, and maintenance schedules.

**Section sources**
- [README.md:37-49](file://README.md#L37-L49)
- [PRODUCT_MODULES.md:1-66](file://docs/PRODUCT_MODULES.md#L1-L66)

### Compliance Frameworks and Localization
OpsTrax includes jurisdiction-aware compliance packs and localization:
- USA: FMCSA ELD/HOS rules, DVIR, driver logs, DOT-ready reporting
- Canada: Transport Canada HOS, bilingual configuration, inspection exports
- Saudi Arabia: TGA/WASL-ready, CST device approval tracking, PDPL privacy controls
- UAE: Transport-Ready, Arabic/English configuration, authority-specific customization
- Pakistan: NTRC transport rules, Urdu language support

Device capability registry defines telemetry and identity capabilities for integrations:
- OBD-II, J1939/CAN, GPS tracker, AI dashcam, temperature sensor, fuel sensor, BLE/RFID driver ID, tire pressure sensor

**Section sources**
- [compliance.registry.ts:1-142](file://backend/src/modules/compliance/compliance.registry.ts#L1-L142)
- [device.registry.ts:1-61](file://backend/src/modules/devices/device.registry.ts#L1-L61)
- [README.md:85-108](file://README.md#L85-L108)

### Multi-Language and Internationalization
The frontend supports five locales with direction-aware rendering:
- English (US), French (Canada), Arabic (Saudi Arabia), Arabic (UAE), Urdu (Pakistan)

The i18n provider synchronizes user preferences with the backend, persists locale in local storage, and sets HTML attributes for accessibility.

**Section sources**
- [README.md:99-108](file://README.md#L99-L108)
- [I18nProvider.tsx:1-66](file://frontend/src/i18n/I18nProvider.tsx#L1-L66)

### Role-Based Access Control (RBAC)
The frontend defines granular permissions and role matrices:
- Permissions include fleet, dispatch, safety, maintenance, compliance, reports, users, settings, and telematics scopes
- Roles include tenant admin, fleet manager, dispatcher, safety manager, maintenance manager, driver, customer, and read-only auditor
- Permission variants normalize punctuation differences to support flexible matching

**Section sources**
- [rbacConfig.ts:1-404](file://frontend/src/auth/rbacConfig.ts#L1-L404)

### AI Copilot and Decision Support
The AI Copilot enables natural language queries across operations:
- Quick starters for dispatch risk, cost leakage, driver coaching, maintenance planning, safety review, customer SLA, executive brief, and compliance audit
- Live evidence feed pulls insights from fleet data
- Action buttons integrate with platform workflows (e.g., dispatch review, ETA updates, maintenance scheduling)

```mermaid
sequenceDiagram
participant User as "User"
participant UI as "AiCopilotPage"
participant API as "Backend API"
participant DB as "PostgreSQL"
User->>UI : "Select category and enter prompt"
UI->>API : "POST /api/ai/ask"
API->>DB : "Query fleet data for insights"
DB-->>API : "Aggregated insights"
API-->>UI : "Response with summary, evidence, next steps"
UI-->>User : "Render assistant response and action buttons"
```

**Diagram sources**
- [AiCopilotPage.tsx:134-358](file://frontend/src/pages/AiCopilotPage.tsx#L134-L358)
- [API_ENDPOINTS.md:15-16](file://docs/API_ENDPOINTS.md#L15-L16)
- [Program.cs:379-381](file://backend-dotnet/Program.cs#L379-L381)

**Section sources**
- [AiCopilotPage.tsx:1-358](file://frontend/src/pages/AiCopilotPage.tsx#L1-L358)
- [API_ENDPOINTS.md:15-16](file://docs/API_ENDPOINTS.md#L15-L16)

### Telemetry and Real-Time Streaming
The Node event service ingests telemetry and safety events and broadcasts them via WebSocket. The .NET API enforces authentication for streaming endpoints using short-lived stream tickets (SST) and validates bearer tokens for protected routes.

```mermaid
sequenceDiagram
participant Dev as "Device"
participant Node as "Node Events"
participant API as ".NET API"
participant WS as "WebSocket Clients"
Dev->>Node : "POST /telemetry/location"
Node->>API : "Persist telemetry"
API-->>WS : "Broadcast event to connected clients"
WS-->>API : "Subscribe via /api/telemetry/stream (?sst=)"
```

**Diagram sources**
- [API_ENDPOINTS.md:23-26](file://docs/API_ENDPOINTS.md#L23-L26)
- [Program.cs:149-172](file://backend-dotnet/Program.cs#L149-L172)

**Section sources**
- [API_ENDPOINTS.md:18-27](file://docs/API_ENDPOINTS.md#L18-L27)
- [Program.cs:149-172](file://backend-dotnet/Program.cs#L149-L172)

### Tenant-Aware Data Model
The database schema supports multi-tenant isolation with tables for companies, users, roles, drivers, vehicles, customers, contracts, assets, and documents. Tenant context is enforced in the backend to ensure data segregation.

```mermaid
erDiagram
COMPANIES {
bigint id PK
varchar company_code UK
varchar name
varchar industry
varchar timezone
varchar status
timestamptz created_at
}
USERS {
bigint id PK
bigint company_id FK
bigint role_id FK
varchar full_name
varchar email UK
varchar role_name
varchar demo_password
varchar password_hash
varchar permissions_json
varchar status
timestamptz created_at
}
DRIVERS {
bigint id PK
bigint company_id FK
varchar driver_code
varchar full_name
varchar phone
varchar email
varchar license_number
date license_expiry
varchar status
decimal safety_score
decimal readiness_score
decimal risk_score
decimal compliance_score
bigint assigned_vehicle_id FK
timestamptz deleted_at
timestamptz created_at
}
VEHICLES {
bigint id PK
bigint company_id FK
varchar vehicle_code
varchar type
varchar make
varchar model
int year
varchar vin
varchar plate_number
varchar status
decimal odometer_miles
decimal readiness_score
decimal data_quality_score
decimal risk_score
varchar device_status
varchar camera_status
bigint assigned_driver_id FK
timestamptz deleted_at
timestamptz created_at
}
CUSTOMERS {
bigint id PK
bigint company_id FK
varchar customer_code
varchar name
varchar contact_name
varchar email
varchar phone
varchar billing_address
varchar shipping_address
varchar status
varchar sla_tier
decimal sla_health_score
decimal delivery_experience_score
decimal risk_score
timestamptz deleted_at
timestamptz created_at
}
ASSETS {
bigint id PK
bigint company_id FK
varchar asset_code
varchar asset_type
varchar name
varchar status
varchar current_location
bigint assigned_vehicle_id FK
bigint assigned_driver_id FK
bigint customer_id FK
varchar current_zone
varchar geofence_status
decimal utilization_score
decimal risk_score
timestamptz deleted_at
timestamptz created_at
}
COMPANIES ||--o{ USERS : "has"
COMPANIES ||--o{ DRIVERS : "has"
COMPANIES ||--o{ VEHICLES : "has"
COMPANIES ||--o{ CUSTOMERS : "has"
COMPANIES ||--o{ ASSETS : "has"
VEHICLES ||--o{ DRIVERS : "assigned"
CUSTOMERS ||--o{ ASSETS : "owns"
```

**Diagram sources**
- [001_schema.sql:1-200](file://database/init/001_schema.sql#L1-L200)

**Section sources**
- [001_schema.sql:1-200](file://database/init/001_schema.sql#L1-L200)

### API Surface and Authentication
- Health and readiness endpoints for Kubernetes and load balancers
- Tenant-aware RBAC enforcement with bearer token validation
- Rate limiting and security headers applied across endpoints
- Swagger UI available for endpoint discovery

```mermaid
flowchart TD
Start(["Incoming Request"]) --> PathCheck["Path and Method Check"]
PathCheck --> UnauthPaths{"Public or Probe?"}
UnauthPaths --> |Yes| Next1["Proceed"]
UnauthPaths --> |No| AuthCheck["Validate Bearer Token"]
AuthCheck --> TokenValid{"Token Valid?"}
TokenValid --> |No| Unauthorized["401 Unauthorized"]
TokenValid --> |Yes| RateLimit["Apply Rate Limit"]
RateLimit --> Allowed{"Within Limits?"}
Allowed --> |No| TooMany["429 Too Many Requests"]
Allowed --> |Yes| RBAC["Resolve Permissions"]
RBAC --> Next2["Execute Handler"]
Next1 --> Next2
Next2 --> End(["Response"])
```

**Diagram sources**
- [Program.cs:92-245](file://backend-dotnet/Program.cs#L92-L245)
- [app.ts:42-72](file://backend/src/app.ts#L42-L72)

**Section sources**
- [Program.cs:257-378](file://backend-dotnet/Program.cs#L257-L378)
- [app.ts:16-97](file://backend/src/app.ts#L16-L97)

## Dependency Analysis
The platform’s runtime dependencies emphasize modern web and cloud-native stacks:
- Frontend: React 19.2, Vite, TanStack Query, React Router, Tailwind CSS, Recharts, Axios
- Backend: ASP.NET Core 8, C# 12, MySQL connector, Swagger/OpenAPI
- Node Events: Node.js 20, Express, ws (WebSocket)
- Database: PostgreSQL schema with JSONB and timestamptz
- Containerization: Docker Compose for local development

```mermaid
graph LR
FE["Frontend (React)"] --> API[".NET API"]
FE --> EVT["Node Events"]
API --> DB["PostgreSQL"]
EVT --> DB
```

**Diagram sources**
- [README.md:24-34](file://README.md#L24-L34)
- [docker-compose.yml:1-45](file://docker-compose.yml#L1-L45)

**Section sources**
- [README.md:24-34](file://README.md#L24-L34)
- [docker-compose.yml:1-45](file://docker-compose.yml#L1-L45)

## Performance Considerations
- Real-time streaming: WebSocket connections reduce polling overhead; short-lived stream tickets mitigate long-lived session exposure.
- Rate limiting: Per-IP windows prevent abuse and protect downstream services.
- Caching: Redis caching for live vehicle state is planned for production enhancements.
- Scalability: Queue/event bus (e.g., RabbitMQ/Kafka) and object storage are future enhancements.

[No sources needed since this section provides general guidance]

## Troubleshooting Guide
Common operational checks:
- Health probes: /health, /health/live, /health/ready, /health/deep
- Readiness: /ready verifies database connectivity
- Swagger: /swagger for endpoint discovery
- Rate limiting: 429 responses indicate window-based throttling
- Authentication: 401 responses for missing or invalid bearer tokens; ensure token validity and user status

**Section sources**
- [Program.cs:257-378](file://backend-dotnet/Program.cs#L257-L378)
- [app.ts:74-89](file://backend/src/app.ts#L74-L89)

## Conclusion
OpsTrax delivers a comprehensive, enterprise-grade platform for connected transport operations. Its modular architecture, robust compliance frameworks, multilingual support, and AI-driven insights enable organizations to optimize dispatch, safety, maintenance, and financial performance while meeting regional regulations and scalability needs.

[No sources needed since this section summarizes without analyzing specific files]

## Appendices

### Target Users and Use Cases
- Fleet operators: real-time visibility, maintenance planning, and cost control
- Dispatchers: dynamic dispatch boards, route optimization, and exception handling
- Safety and compliance managers: incident tracking, DVIR workflows, and regulatory audits
- Finance teams: fuel/idling analytics, profitability, and SLA/KPI reporting
- Drivers: personal HOS tracking, coaching, and POD workflows
- Customers: self-service portals for ETA and shipment tracking

Deployment Scenarios:
- Local development: Docker Compose with ports for frontend, API, and Node events
- Demo environments: pre-seeded data and standardized credentials
- Production: containerized deployments with ingress, SSL termination, and persistent storage

**Section sources**
- [README.md:53-81](file://README.md#L53-L81)
- [README.md:117-142](file://README.md#L117-L142)