# Zayra Feature Coverage

This tracker keeps the build review moving feature by feature. A feature is considered production-ready only when the database/API contract, UI integration, empty/error states, and verification steps are covered.

## Current Baseline

| Area | Status | Notes |
| --- | --- | --- |
| Frontend build | Passing | `npm --prefix frontend run build` succeeds. Bundle warning remains from chart/icon libraries. |
| .NET API build | Passing | `dotnet build backend-dotnet/Zayra.Api/Zayra.Api.csproj` succeeds. |
| API tests | Passing | `dotnet test backend-dotnet/Zayra.Api.Tests/Zayra.Api.Tests.csproj` passes 8 tests. |
| Node AI service start | Passing | `npm --prefix backend-node run start` starts without module-type warnings. |
| Git repository | Missing | This folder is not initialized as a git repository yet. |

## Feature 1: Dashboard Summary

| Coverage Item | Status | Evidence |
| --- | --- | --- |
| Backend endpoint | Done | `GET /api/dashboard/summary` reads employees and attendance records through EF Core. |
| Static demo data removed from main metrics | Done | Dashboard cards, attendance pie, overtime total, churn risk, and AI insight copy now use API data. |
| Loading state | Done | Hero copy shows loading while the summary request is in flight. |
| Error state | Done | API failures render a visible dashboard error banner. |
| Build verification | Done | Frontend and .NET builds pass. |
| Automated tests | Done | API tests cover summary metrics and monthly trend aggregation. |
| Historical chart data | Done | `GET /api/dashboard/trends` powers monthly attendance and overtime charts. |

## Recommended Next Feature Order

1. Employee master: add DTO validation, search/filter/pagination, duplicate employee-code protection, and frontend CRUD flow.
2. Attendance: add clock-in/import APIs, daily status summaries, and validation for duplicate employee/date records.
3. Leave: add leave request lifecycle, approvals, balances, and dashboard integration.
4. Tenant enforcement across workforce modules: add authenticated users, organizations, role-based access, and tenant scoping across all queries.

## Feature 2: Authentication and RBAC

| Coverage Item | Status | Evidence |
| --- | --- | --- |
| Clean Architecture structure | Done | Auth split across Domain, Application, Infrastructure, and Controllers. |
| JWT auth | Done | Bearer auth configured in Program.cs and Swagger. |
| Refresh tokens | Done | Refresh tokens are hashed, stored, rotated, and revoked. |
| RBAC | Done | Role claims, permission claims, seeded roles/permissions, and Admin management endpoints. |
| Multi-tenant support | Done | Tenant entity, tenant-scoped users/roles, and tenant claims. |
| Audit logs | Done | Auth and RBAC workflows write audit logs. |
| Forgot/reset password | Done | Reset tokens are hashed and active sessions are revoked after reset. |
| Default admin seed | Done | Startup seeder creates default tenant/admin/roles/permissions. |
| Automated tests | Done | Auth tests cover password hashing, login, refresh rotation, RBAC permissions, and audit logging. |
| Frontend integration | Done | React app now has login, logout, refresh-token retry, forgot/reset password, and authenticated dashboard API calls. |

## Feature 3: Employee Management

| Coverage Item | Status | Evidence |
| --- | --- | --- |
| Employee draft lifecycle | Done | Draft create/update/submit/HR approve endpoints and frontend draft workflow. |
| Activation workflow | Done | HR approval generates employee ID, creates user account, activates employee, and writes history. |
| GCC profile fields | Done | KSA/UAE/Qatar/Kuwait/Oman/Bahrain identity, visa, sponsorship, passport, WPS, and residency fields are modeled. |
| Documents | Done | Metadata endpoint, multipart upload, local binary storage, download endpoint, and frontend upload flow are implemented. |
| Payroll/shift/leave policies | Done | Draft and employee profile support payroll profile, shift policy, and leave policy assignment. |
| Sensitive updates | Done | Sensitive field changes create approval requests and history rather than direct updates. |
| Transfers | Done | Current manager, new manager, and HR approval flow updates employee profile and history. |
| Reports | Done | Headcount, active, new joiners, exits, probation, department, branch, nationality, gender, expiry, and incomplete profile counts. |
| AI employee search | Done | Rule-based natural language endpoints for expiry, missing bank details, probation, and incomplete onboarding risk. |
| Frontend module | Done | Navigation renamed to Employees and includes draft creation, approval, reports, AI query, and directory. |
| Notifications | Done | In-app notification records, notification API, and frontend notification panel are implemented. Email/SMS can be added as delivery channels later. |
| Localized templates/Hijri dates | Done | Contract/offer/sponsorship template rendering and Um Al-Qura Hijri conversion APIs are implemented and surfaced in the Employees UI. |
| Fine-grained permissions | Done | Sensitive field view/edit checks use role and permission claims; new document/template/notification/localization permissions are seeded. |
| Automated tests | Done | Tests cover activation and sensitive-change approval behavior. |

## Feature 4: Backend Foundation Modules

| Coverage Item | Status | Evidence |
| --- | --- | --- |
| Company setup | Done | Tenant-scoped company entity, DTOs, service, controller endpoints, audit logs, schema, and seed data. |
| Branch setup | Done | Tenant-scoped branch entity with company relationship, GCC fields, service/controller endpoints, schema, and seed data. |
| Department management | Done | Department entity supports branch, parent department, manager, bilingual names, service/controller endpoints, and schema. |
| Designation management | Done | Designation entity supports department, grade, manager-role flag, bilingual titles, service/controller endpoints, and schema. |
| Approval workflow engine | Done | Workflow/steps/request/decision entities, service, controller endpoints, audit logs, seed workflows, and tests. |
| Multi-tenancy | Done | Organization and approval modules enforce tenant-scoped queries and unique tenant codes. |
| Seed data | Done | Default company, Dubai HQ branch, HR department, HR Officer designation, onboarding workflow, and transfer workflow. |
| Automated tests | Done | Tests cover tenant-scoped organization data and multi-step approval progression. |
