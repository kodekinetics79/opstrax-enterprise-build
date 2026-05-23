# Zayra Authentication Service

The .NET 8 API now includes an enterprise authentication slice using Clean Architecture folders inside the current API project:

- `Domain/Entities`: tenants, users, roles, permissions, refresh tokens, password reset tokens, audit logs
- `Application/Auth`: DTOs, options, and service interfaces
- `Infrastructure/Auth`: JWT, password hashing, login/refresh/logout/reset workflows, RBAC management
- `Infrastructure/Audit`: audit writer
- `Infrastructure/Seed`: default tenant/admin/roles/permissions seeding
- `Controllers`: HTTP API for auth, RBAC, and audit logs

## Default Seed

When the API starts, it attempts to create the auth schema through EF `EnsureCreated` and seed:

- Tenant: `zayra`
- Admin email: `admin@zayra.local`
- Admin password: `ChangeMe123!`
- Roles: `Admin`, `HR Manager`, `Employee`

Change these values before production in `backend-dotnet/Zayra.Api/appsettings.json` or environment-specific configuration.

## Endpoints

| Method | Path | Auth | Purpose |
| --- | --- | --- | --- |
| `POST` | `/api/auth/login` | Public | Login with email/password/tenant and receive JWT + refresh token |
| `POST` | `/api/auth/refresh` | Public | Rotate refresh token and issue a new JWT |
| `POST` | `/api/auth/logout` | JWT | Revoke a refresh token |
| `POST` | `/api/auth/forgot-password` | Public | Create a password reset token |
| `POST` | `/api/auth/reset-password` | Public | Reset password and revoke active refresh tokens |
| `GET` | `/api/auth/me` | JWT | Return current user, tenant, roles, permissions |
| `GET` | `/api/access/roles` | Admin | List roles for current tenant |
| `GET` | `/api/access/permissions` | Admin | List permissions |
| `POST` | `/api/access/users` | Admin | Create a tenant user and assign roles |
| `PUT` | `/api/access/users/{userId}/roles` | Admin | Replace a user's roles |
| `GET` | `/api/audit-logs` | Admin | Read recent tenant audit events |

## Security Notes

- Passwords are hashed with PBKDF2-SHA256 using per-password salts and 100,000 iterations.
- Refresh and reset tokens are stored only as SHA-256 hashes.
- Login, refresh, logout, password reset, user creation, and role assignment write audit logs.
- Forgot-password currently returns the reset token so the API is testable without email infrastructure. Before launch, connect this to email delivery and stop returning the token to clients.
- JWT signing key in `appsettings.json` is a development default and must be replaced by a secret manager or environment variable before production.
