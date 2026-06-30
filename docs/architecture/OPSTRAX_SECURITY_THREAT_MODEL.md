# OpsTrax Security Threat Model

## Key Risks
- Tenant data leakage
- RBAC bypass
- Platform admin and tenant admin cross-contamination
- Session hijacking
- AI prompt/data leakage
- Webhook replay attacks
- API key exfiltration
- Customer portal exposure
- Media/file access abuse
- Finance tampering
- IoT command abuse

## Required Controls
- Strong bearer/session handling with CSRF for browser actions
- Tenant scoping on every query path
- Separation of platform and tenant identities
- Credential isolation for integrations
- Replay protection for webhooks and device ingestion
- Approval gates for finance, contract, and IoT command actions
- Immutable audit logging for privileged actions

