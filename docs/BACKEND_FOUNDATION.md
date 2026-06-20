# Backend Foundation

KynexOne backend foundation now covers the SaaS-ready primitives required before building deeper HR, payroll, attendance, and compliance modules.

Implemented modules:

- Authentication, JWT refresh tokens, password reset, RBAC, seeded tenant admin
- Tenant-scoped audit logs
- Company setup with GCC registration, WPS, GOSI, and Qiwa identifiers
- Branch setup with country, city, labor office, timezone, and head-office flag
- Department setup with branch, hierarchy, bilingual names, and manager assignment
- Designation setup with department, job grade, bilingual title, and manager-role flag
- Employee management with GCC identity fields, documents, sensitive approvals, reports, AI queries, templates, and Hijri conversion
- Reusable approval workflow engine with workflow steps, approval requests, approval decisions, audit logging, and seeded onboarding/transfer workflows

Primary API areas:

- `POST /api/auth/login`
- `GET /api/access/roles`
- `GET /api/audit-logs`
- `GET|POST /api/organization/companies`
- `GET|POST /api/organization/branches`
- `GET|POST /api/organization/departments`
- `GET|POST /api/organization/designations`
- `GET|POST /api/approval-workflows`
- `GET|POST /api/approval-workflows/requests`
- `POST /api/approval-workflows/requests/{requestId}/decide`
- `GET|POST /api/employees/...`

Next backend modules should be built in this order:

1. Attendance policies, shifts, clock-in/out, import, exceptions, and approval handoff.
2. Leave policies, leave balances, leave requests, encashment, and payroll impact.
3. Payroll profiles, salary components, WPS/GOSI calculations, payroll runs, payslips, and bank export.
4. Compliance calendar, visa/iqama renewals, contract renewals, document expiry, and notification escalations.
5. AI workforce intelligence service integration for anomaly detection, natural language search, and predictive alerts.
