# OpsTrax — Enterprise Architecture: Resilience, Storage & Data Protection

This document covers the architectural basics an enterprise buyer / security review
expects: object storage, failover, disaster recovery, backup, data protection,
CDN/WAF, and residency. It states **what is now in the code**, **what you must
provision** (account-level, cost-incurring — your decision, not something the app
can do for itself), and the **recommended stack** for the current Vercel + Render +
Neon deployment.

Legend: ✅ built in code · 🟢 provision (wire an account) · 📄 documented/drill.

---

## 0. Current deployment topology

```
 Browser ──HTTPS──▶ Vercel (SPA, global CDN) ──API calls──▶ Render (.NET Opstrax.Api)
                                                                │
                                          ┌─────────────────────┼───────────────────┐
                                          ▼                     ▼                    ▼
                                    Neon Postgres        Object store         (AI provider)
                                    (primary + replica)  (R2 / S3)
```

---

## 1. Object storage ✅ (code) + 🟢 (provision)

**Built:** `IObjectStore` (S3-compatible) + `FileStorageService`. Real multipart
upload (`POST /api/documents/upload`), authenticated tenant-scoped download
(`/api/documents/{id}/download`, `/api/files/{key}`), signed URLs, size/type
validation, and erasure/retention hooks. The old placeholder endpoints are
superseded. Without credentials it falls back to a **non-durable local store**,
which the Reliability Center flags as `degraded`.

**Recommended:** **Cloudflare R2**.
- Why over AWS S3: **zero egress fees** (fleet apps serve many POD photos), S3-API
  compatible (no code change — just `STORAGE_ENDPOINT`), cheap, and it can sit
  behind Cloudflare's CDN/WAF for free.
- Alternative: AWS S3 if you standardize on AWS/KMS; Backblaze B2 for lowest storage cost.
- Vercel Blob is fine for the SPA but R2/S3 is the better fit for a .NET backend on Render.

**Provision:** create an R2 bucket (private), an API token, then set
`STORAGE_BUCKET`, `STORAGE_ACCESS_KEY`, `STORAGE_SECRET_KEY`, `STORAGE_ENDPOINT`
in Render. Server-side encryption is requested automatically.

---

## 2. Customer data protection ✅ (code) + 🟢 (KMS optional)

**Built:**
- **PII encryption at rest** — `PiiProtectionService` (AES-256-GCM envelope), applied
  to driver `license_number` end-to-end (encrypt on write, decrypt on read, **blind
  index** for the uniqueness lookup, **crypto-shred** on erasure). Key rotation
  supported via `DATA_ENCRYPTION_KEY_PREVIOUS`.
- **KMS-swappable** — `IDataKeyProvider` isolates key material; the default reads an
  env key, but you can drop in AWS KMS / HashiCorp Vault with no call-site changes.
- **DSR** — export decrypts to give the subject their real data; erasure anonymizes +
  crypto-shreds. Already tenant-isolated by Postgres **RLS** (Stage-19/20).
- **Log redaction** — `LogRedactor` scrubs tokens/PII/secrets from every log line.

**Provision:** `DATA_ENCRYPTION_KEY=$(openssl rand -base64 32)` in Render. For a SOC 2
/ ISO 27001 audit, move the key into **AWS KMS** or **Vault** (implement
`IDataKeyProvider`) so the key is never in an env var.

**Staged rollout (documented gap):** encryption is proven on `license_number`. Extend
the same pattern to `email`/`phone`/`contact_name` on `drivers`/`customers` — note
these appear in list/search/join queries, so each needs a blind index + decryption at
its read sites (a controlled, test-covered migration, not a blind sweep).

---

## 3. Database failover / bypass ✅ (code) + 🟢 (provision)

**Built:** `Database.OpenReadAsync()` prefers a **read replica** when
`PG_CONNECTION_REPLICA` is set and **auto-falls back to the primary** if the replica
is unreachable — read traffic survives a replica blip. Primary opens already retry
transient failures with backoff (Neon cold-start tolerance). Replica posture is shown
in the Reliability Center → Data protection component.

**Recommended:** enable a **Neon read replica** (compute-only branch off the same
storage — no data copy, near-zero lag) and set `PG_CONNECTION_REPLICA`. Neon's storage
is multi-AZ within a region by design, so the primary itself is not a single-disk SPOF.

**Provision:** Neon console → add a read replica → copy its connection string to
`PG_CONNECTION_REPLICA`.

---

## 4. Disaster recovery + backup 📄 (drill) + 🟢 (verify plan)

**Built:** `tools/dr-restore-drill.sh` — restores the DB to a point in time on a
throwaway **Neon branch** (copy-on-write, zero risk to prod), asserts core row counts,
and reports measured **RPO/RTO**. Result is recorded in Platform Admin → Backup
Verifications.

**Backup model:** Neon provides continuous WAL + **point-in-time restore**. Confirm the
retention window (Neon → Settings → History) meets your RPO commitment.

**Targets to commit to (and prove with the drill):**
- **RPO** ≤ 5 min (Neon PITR granularity is seconds; the drill defaults to a 60-min restore point).
- **RTO** ≤ 30 min (branch-restore is typically minutes; the drill measures it).

**Run quarterly:** `NEON_PROJECT_ID=... ./tools/dr-restore-drill.sh`

---

## 5. Downtime reporting *ahead of* an outage ✅ (code)

**Built:** the **error-budget burn-rate** signal (`SloService.EvaluateBurnRate`, in the
Reliability Center). Using the Google SRE multi-window method it fires **while the
service is still up but trending to breach**:
- burn ≥ **14.4×** → `critical / page_now` (30-day budget gone in ~2h)
- burn ≥ **6×** → `high / alert`
- burn ≥ **3×** → `medium / watch`

Combined with the 8 alert rules (`SloService.AlertRules`) and `/metrics`, an external
monitor can page **before** users see a full outage. Predictive *capacity* forecasting
(disk/connection trend) is the next step and is not yet built.

---

## 6. CDN / WAF / DDoS 🟢 (provision) — the main thing still to wire

**State:** the **SPA already has a CDN** (Vercel, global edge, free). The **API on
Render has none** — no CDN, no WAF, no managed DDoS.

**Recommended:** put **Cloudflare** in front of the Render API hostname.
- Free tier gives DDoS mitigation + a WAF (managed rules, rate limiting, bot control).
- Same account can front R2 for public-safe asset delivery.
- Alternative: migrate the API onto **Vercel Functions** (.NET is not first-class there;
  not recommended for this backend) — Cloudflare-in-front is the lower-risk path.

**Provision:** add the API domain to Cloudflare → proxy (orange-cloud) → enable WAF
managed ruleset + a rate-limit rule on `/api/auth/login`. The app already sets security
headers, CSRF, and per-IP rate limiting as defence in depth.

---

## 7. Data residency (PDPL / PIPEDA) 🟢 (provision) + 📄

**Requirement:** Saudi (PDPL) data should stay in-region or use an approved transfer;
Canadian (PIPEDA) similarly. **Action:** pin the **Neon region**, the **Render region**,
and the **R2 jurisdiction** to the target geography and document it in the DPA. For
multi-region tenants, run separate regional deployments (the app is single-region today).

---

## Provisioning checklist (in priority order)

| # | Action | Where | Env var(s) | Priority |
|---|---|---|---|---|
| 1 | Set PII data key | Render | `DATA_ENCRYPTION_KEY` | 🔴 now (compliance) |
| 2 | Create R2 bucket + token | Cloudflare | `STORAGE_BUCKET/ACCESS_KEY/SECRET_KEY/ENDPOINT` | 🔴 now (uploads real) |
| 3 | Enable Neon read replica | Neon | `PG_CONNECTION_REPLICA` | 🟠 soon (resilience) |
| 4 | Put Cloudflare in front of API | Cloudflare | — | 🟠 soon (WAF/DDoS) |
| 5 | Run DR drill; record RPO/RTO | CLI | `NEON_PROJECT_ID` | 🟠 quarterly |
| 6 | Enable retention worker | Render | `RetentionWorker__Enabled=true` | 🟡 after policy review |
| 7 | Move key to KMS | AWS/Vault | implement `IDataKeyProvider` | 🟡 for SOC 2 audit |
| 8 | Pin regions + DPA | Neon/Render/R2 | — | 🟡 residency |

Everything in code is deployed and defaults safe: with **no** provisioning the app
still runs (local file store, PII pass-through) and the **Reliability Center flags each
unconfigured pillar as degraded** — so the gaps are visible, never silent.
