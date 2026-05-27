# OpsTrax — Next Production Roadmap

**Developer:** Kode Kinetics  
**Starting point:** Enterprise Demo Build (2026-05-24)

This document outlines the recommended production readiness milestones beyond the Enterprise Demo build.

---

## Phase 1 — Security & Auth Hardening (Priority: Critical)

| Task | Detail |
|---|---|
| Replace demo tokens with signed JWTs | ASP.NET Core `ITokenService`, RS256 or HS256, expiry + refresh |
| Password hashing | Replace `demo_password` with bcrypt/Argon2 hashed passwords |
| Row-level multi-tenant isolation | Inject `company_id` from JWT claims into all DB queries via middleware |
| HTTPS enforcement | TLS termination at Nginx; redirect HTTP → HTTPS |
| Rate limiting | ASP.NET Core rate limiting middleware on auth and write endpoints |
| CORS lockdown | Restrict `AllowedOrigins` to production domain only |
| Secrets management | Move all connection strings and keys to environment variables / Vault |

---

## Phase 2 — Live Data Integrations (Priority: High)

| Integration | Provider Options |
|---|---|
| Telematics / GPS | Samsara, Motive (KeepTruckin), Verizon Connect, Geotab, Trimble |
| ELD (US) | Samsara ELD, Motive ELD (FMCSA registered) |
| Dashcam | Samsara AI Dashcam, Lytx, Netradyne |
| Mapping tiles | Mapbox GL JS or Google Maps Platform |
| Email / SMS | AWS SES + Twilio (or SendGrid) |
| Push notifications | Firebase Cloud Messaging (FCM) |
| Document storage | AWS S3, Azure Blob Storage, or MinIO (self-hosted) |

---

## Phase 3 — AI / LLM Integration (Priority: High)

| Task | Detail |
|---|---|
| AI Copilot live prompts | Server-side proxy to Claude (Anthropic) or GPT-4o (OpenAI) |
| RAG / context injection | Inject fleet KPIs, open alerts, and recent events into each prompt |
| Streaming responses | Server-Sent Events (SSE) or WebSocket streaming for copilot |
| AI recommendation engine | Replace seeded recommendations with LLM-generated ones on schedule |
| Predictive maintenance | Connect to vehicle telemetry for ML-based maintenance forecasting |

---

## Phase 4 — Payments & Subscription (Priority: Medium)

| Task | Detail |
|---|---|
| Stripe Billing integration | Plans, seats, usage-based billing, invoice generation |
| Subscription enforcement | API middleware checks active subscription before allowing writes |
| White-label tenant billing | Per-reseller billing accounts, revenue share |

---

## Phase 5 — Multi-Tenant SaaS Hardening (Priority: Medium)

| Task | Detail |
|---|---|
| Tenant provisioning flow | Self-service signup → auto-create company, seed demo data |
| White-label branding | Per-tenant logo, color scheme, custom domain |
| Tenant data isolation audit | Full query audit for `company_id` enforcement |
| Super-admin console | Kode Kinetics ops dashboard for tenant management |

---

## Phase 6 — Performance & Observability (Priority: Medium)

| Task | Detail |
|---|---|
| Query optimization | Add indexes on `company_id`, `deleted_at`, `status`, `created_at` |
| API response caching | Redis for high-frequency read endpoints (command center, KPIs) |
| Structured logging | Serilog → Seq or ELK stack |
| APM | OpenTelemetry → Datadog / New Relic |
| Frontend bundle splitting | Route-level code splitting, lazy imports |
| CDN | Static asset delivery via CloudFront or Cloudflare |

---

## Phase 7 — Regulatory & Compliance Productization

| Task | Detail |
|---|---|
| FMCSA ELD AOBRD compliance | Partner with certified ELD provider for US market |
| Transport Canada ELD | Partner or build to TC ELD mandate spec (2023+) |
| Saudi MOT compliance | Connect to Wasl platform APIs |
| UAE RTA integration | Connect to RTA/Nafis fleet authority APIs |
| Pakistan NTRC | Connect to NTRC vehicle registration APIs |
| GDPR / Privacy | Data retention policies, right-to-erasure, DPA agreements |

---

## Phase 8 — Mobile Applications

| Task | Detail |
|---|---|
| Driver mobile app | React Native — HOS, DVIR, jobs, messaging, offline mode |
| Mechanic mobile app | Work orders, DVIR review, parts lookup |
| Customer tracking app | Real-time ETA, proof of delivery, communication |

---

## Estimated Timeline

| Phase | Effort | Priority |
|---|---|---|
| Phase 1 — Security | 2–3 weeks | Critical |
| Phase 2 — Live Integrations | 6–10 weeks | High |
| Phase 3 — AI/LLM | 3–4 weeks | High |
| Phase 4 — Payments | 2–3 weeks | Medium |
| Phase 5 — Multi-Tenant | 3–4 weeks | Medium |
| Phase 6 — Observability | 2–3 weeks | Medium |
| Phase 7 — Regulatory | Ongoing | Market-dependent |
| Phase 8 — Mobile | 8–12 weeks | Strategic |

---

## Contact

To discuss production deployment, custom integrations, or enterprise licensing:

**Kode Kinetics**  
[www.kodekinetics.com](https://www.kodekinetics.com)  
info@kodekinetics.com  
+1 571 430 5333

---

*Roadmap maintained by Kode Kinetics — last updated 2026-05-24*
