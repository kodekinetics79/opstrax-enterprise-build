# OpsTrax — Known Limitations

**Build:** Enterprise Demo Build  
**Developer:** Kode Kinetics

This document describes current limitations of the Enterprise Demo build. These are intentional design boundaries for the demo environment, not defects.

---

## 1. AI / ML — No Live LLM

- The AI Copilot module uses seeded response patterns and static prompt templates.
- There is no connection to OpenAI, Anthropic, or any external LLM.
- AI recommendations shown throughout the platform are pre-seeded demo data.
- **Production path:** Connect to an LLM provider API (OpenAI GPT-4o, Claude, etc.) via a server-side proxy.

---

## 2. Maps — No Live Tile Layer

- The Control Tower / Live Map renders vehicle pins using simulated lat/lng coordinates from the database.
- There is no connection to Google Maps, Mapbox, or any tile provider.
- Map tiles would require a paid API key in production.
- **Production path:** Add Mapbox GL JS or Google Maps API with a billing account.

---

## 3. ELD / Telematics — No Live Device Integration

- HOS / ELD data is fully seeded demo data.
- No physical ELD device or telematics provider (Samsara, Motive, Verizon Connect, etc.) is connected.
- OpsTrax does not claim FMCSA certification or any regulatory ELD approval.
- **Production path:** Integrate telematics provider SDK/webhook for live GPS, HOS, and dashcam data.

---

## 4. Email / SMS / Push — No Notifications Sent

- Customer ETA notifications, driver coaching alerts, and compliance warnings are recorded in the database but no actual email/SMS/push is sent.
- **Production path:** Add SMTP (SES, SendGrid) or Twilio integration.

---

## 5. Authentication — Demo-Only Tokens

- Tokens are base64-encoded GUIDs, not signed JWTs.
- There is no token expiry or refresh flow in the demo.
- Passwords are stored as plain demo values in the `demo_password` column.
- **Production path:** Replace with ASP.NET Core Identity + JWT with expiry/refresh. Hash passwords with bcrypt/Argon2.

---

## 6. File Uploads — No Object Storage

- Document upload, proof of delivery photos, and dashcam video evidence use placeholder records only.
- No files are actually stored.
- **Production path:** Add S3-compatible object storage (AWS S3, MinIO, Azure Blob).

---

## 7. Payments / Billing — No Stripe

- The Billing / Subscription module shows plan and seat data from the database.
- No Stripe or payment processor integration is present.
- **Production path:** Add Stripe Billing with webhook handling.

---

## 8. Multi-Tenant Isolation — Demo Has Single Tenant

- The database schema supports multi-tenancy (`company_id` foreign keys throughout).
- The demo runs as a single company (`company_id = 1`).
- Row-level tenant isolation is not enforced in API queries.
- **Production path:** Add tenant middleware to inject `company_id` from the authenticated session into all queries.

---

## 9. Regulatory Compliance — Framework Only

- OpsTrax provides compliance management, monitoring, and audit-readiness tools.
- **Final regulatory compliance remains the carrier's / operator's responsibility.**
- HOS rule engines (US, Canada, SA, AE, PK) compute based on seeded data.
- ELD certification depends on the connected ELD device/provider and applicable country requirements.

---

## 10. Node Events — Demo Simulation Only

- The Node Events server (port 8090) generates simulated fleet events.
- Events do not originate from real vehicles or devices.
- WebSocket clients can subscribe but will receive synthetic data only.

---

*Document maintained by Kode Kinetics — last updated 2026-05-24*
