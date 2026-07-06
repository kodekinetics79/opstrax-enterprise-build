# OpsTrax: Simplified Login with RBAC & CSRF Protection

## Overview
This document outlines the simplified login implementation with integrated Role-Based Access Control (RBAC) and Cross-Site Request Forgery (CSRF) protection.

## Architecture

### Backend (C# / .NET)

#### CSRF Middleware
- **File**: `Middleware/CsrfMiddleware.cs`
- **Functionality**:
  - Generates CSRF tokens for each session
  - Validates tokens on state-changing requests (POST, PUT, DELETE)
  - Uses `HttpOnly` cookies and header validation
  - Token format: Base64-encoded 32-byte random value

```csharp
// Token generated and validated automatically for all requests
// Pattern: Cookie token must match X-CSRF-Token header
```

#### Login Endpoint
- **Route**: `POST /api/auth/login`
- **Signature**: `Login(HttpContext http, LoginRequest request, Database db, AuditService audit, CancellationToken ct)`
- **Returns**:
```json
{
  "success": true,
  "data": {
    "token": "base64_session_token",
    "csrfToken": "base64_csrf_token",
    "user": { "id", "email", "name" },
    "role": "company_admin",
    "company": { "name", "code" },
    "permissions": ["dashboard.view", "dispatch.view", ...]
  },
  "message": "Login successful"
}
```

### Frontend (React / TypeScript)

#### Simplified Login UI
- **File**: `pages/LoginPage.tsx`
- **Features**:
  - Clean, minimal form design
  - Email/password inputs with show/hide toggle
  - Quick access demo users
  - Error handling with clear messaging
  - Security notice footer

#### CSRF Token Management
- **File**: `hooks/useCsrf.tsx`
- **Exports**:
  - `useCsrfToken()`: Get/set CSRF token from session
  - `getGlobalCsrfToken()`: Access global CSRF token store
  - `setGlobalCsrfToken(token)`: Update global token

#### API Client with CSRF Interceptor
- **File**: `services/apiClient.ts`
- **Interceptors**:
  1. **Request**: Adds `X-CSRF-Token` header for non-GET requests
  2. **Response**: Captures and stores CSRF token from response headers

```typescript
// Example: Automatic CSRF handling
const response = await apiClient.post("/api/some-endpoint", data);
// CSRF token is automatically included in X-CSRF-Token header
```

#### Auth API Service
- **File**: `services/authApi.ts`
- **Function**: `login(usernameOrEmail, password)`
- **Behavior**:
  1. Calls `/api/auth/login` endpoint
  2. Extracts and stores CSRF token
  3. Falls back to demo auth if enabled
  4. Returns complete `UserSession`

#### Session Management
- **File**: `hooks/useAuth.tsx`
- **Updates**: Now includes `csrfToken` in session storage
- **TTL**: 8 hours (matches backend)

### RBAC Permission System

#### Permission Hooks
- **File**: `hooks/usePermission.tsx`
- **Functions**:
  - `useHasPermission()`: Check single permission with wildcard support
  - `usePermissions()`: Get all user permissions
  - `useHasAnyPermission(perms[])`: Check if user has any permission
  - `useHasAllPermissions(perms[])`: Check if user has all permissions

#### Permission Patterns
```typescript
// Exact match
hasPermission("dashboard.view")

// Wildcard pattern
hasPermission("dashboard.*")  // Covers dashboard.view, dashboard.manage, etc.

// Super admin
permissions: ["*"]  // Has all permissions

// Role-based default
role: "company_admin" → permissions: ["dashboard.view", "map.view", ...]
```

#### Permission Guard Component
```tsx
<PermissionGuard permission="dashboard.view">
  <DashboardComponent />
</PermissionGuard>
// Redirects to /live-dashboard if permission denied
```

## Configuration

### Role Permissions Mapping
- **File**: `auth/rbacConfig.ts`
- **Roles**:
  - `platform_super_admin`: Global access (`*`)
  - `company_admin`: Full company access
  - `operations_manager`: Operations oversight
  - `dispatcher`: Dispatch management
  - `fleet_manager`: Fleet assets
  - `driver`: Field operations
  - `safety_compliance_manager`: Safety controls
  - `maintenance_manager`: Maintenance planning
  - `finance_billing_manager`: Financial records
  - `crm_sales_manager`: Customer management
  - `customer_portal_user`: Portal access
  - `vendor_service_provider`: Vendor workflows

### Demo Users
- **File**: `auth/demoUsers.ts`
- **Available**: 12 demo users covering all roles
- **Password**: provisioned out-of-band; never documented or committed

## Security Features

### CSRF Protection
✅ Token-based validation
✅ HttpOnly cookie storage
✅ SameSite=Strict mode
✅ 8-hour token expiration
✅ Automatic refresh on response

### Session Management
✅ Base64-encoded session tokens
✅ Server-side session persistence
✅ 8-hour session expiration
✅ Automatic token rotation on re-login

### Permission Validation
✅ Compiled permission bitmasks (optimized)
✅ Role-based defaults with override support
✅ Wildcard permission patterns
✅ Client & server-side validation

### Audit Logging
✅ User login events logged
✅ Permission checks audited
✅ Session creation tracked

## Usage Guide

### 1. Login Flow
```typescript
// User enters email and password
const session = await authApi.login(email, password);
// CSRF token automatically extracted and stored
setSession(session);
// User redirected to dashboard
```

### 2. Permission Check
```typescript
// In component
const hasPermission = useHasPermission();

if (!hasPermission("dashboard.view")) {
  return <Navigate to="/login" />;
}
```

### 3. Protected API Call
```typescript
// CSRF token automatically added
const result = await apiClient.post("/api/endpoint", data);
// Equivalent to: POST with X-CSRF-Token header
```

## Pilot Credential Provisioning

No shared or static credentials are maintained in source control. Platform operators
use the one-time invite flow. Tenant users are provisioned by an authorized tenant
administrator, and customer portal users must additionally be bound to a customer.
Passwords and MFA enrollment data are delivered out-of-band.

## Troubleshooting

### CSRF Token Mismatch (403)
**Symptom**: `CSRF token validation failed`
**Solution**: 
1. Ensure `X-CSRF-Token` header is set for POST/PUT/DELETE
2. Check token matches cookie value
3. Verify `withCredentials: true` in axios config

### Permission Denied (Redirect)
**Symptom**: User redirected to dashboard despite login
**Solution**:
1. Check user's role in RBAC config
2. Verify permission string matches required permission
3. Check wildcard pattern (e.g., `dashboard.*`)

### Session Expired
**Symptom**: Logged out after inactivity
**Solution**:
1. Re-login to obtain new session
2. Check server logs for 8-hour expiration
3. Implement refresh token if longer sessions needed

## Performance Optimization

- **Permission checks**: O(1) with array includes
- **Token validation**: O(1) string comparison
- **Session storage**: LocalStorage (browser cache)
- **Wildcard matching**: Pre-compiled patterns

## Future Enhancements

- [ ] Refresh token implementation
- [ ] Permission change notifications
- [ ] Rate limiting on login attempts
- [ ] Two-factor authentication (2FA)
- [ ] Device fingerprinting
- [ ] Permission audit log export
