# ADR-008 — Configurable Multi-Tenant Financial Platform (master)

**Status:** Accepted (architecture). Synthesizes six specialist designs (configurable rating,
revenue-recognition/accounting-methods, flexible billing, settlement/AP, accounting-tax-payments
**integration**, and the config **meta-architecture**). Supersedes the fixed ADR-007 rating/billing/
settlement designs by generalizing them onto one per-tenant config engine.

## The two decisions that shape everything

1. **Build the operational financial engine; integrate the accounting stack.** OpsTrax OWNS
   rating, charges, invoicing, and settlement (its differentiator). It does NOT rebuild the general
   ledger, tax logic, or money movement — it **integrates** QuickBooks/Xero/NetSuite (GL),
   Avalara/TaxJar/ZATCA (tax), Stripe/Bill.com (payments). OpsTrax is system-of-record through the
   **issued invoice + reconciled cash position**; the GL owns the accounting. This avoids becoming
   an accounting-software vendor and minimizes the money-math *we* must prove correct.

2. **Tenant behavior is CONFIG, never code.** Every tenant's financial behavior is a **versioned,
   effective-dated, immutable-once-published config document graph** resolved by `(company_id, date)`.
   Every priced or posted record pins the exact config version it used, so replays reproduce old
   numbers. The engine contains **zero tenant-specific branches**.

## The unifying layer — `fin_config_sets` (the config envelope)

One resolver, `FinancialConfigResolver`, is the only reader. Pure, cache-forever (published versions
are immutable). Resolution:
- **Live path:** the single `published` envelope where `effective_from <= D < effective_to` (a GiST
  exclusion constraint guarantees 0-or-1, so it can't overlap; 0 → fail-closed, no invented config).
- **Replay path:** when a record carries a pinned `config_set_id`, resolve *that* exactly — so
  re-running a closed period reproduces its numbers regardless of later rate changes.

Hanging off each envelope (each row `company_id` + `config_set_id`): `rating_programs` (wraps the
existing `rate_cards`), `billing_profiles`, `pay_programs`, `tax_profiles`, `payment_profiles`,
`gl_mapping_rules`, `accounting_calendars`. Publishing is approval-gated (`finance.config.publish`,
high-risk) with **author ≠ publisher** (maker-checker), an append-only `fin_config_change_log`, and a
mandatory **dry-run diff** ("142 jobs would re-rate; ΔARR +$3,110; 3 now fail-closed") before publish.

### Safe rule evaluation — no code execution
Hybrid, stored as validated JSON AST:
- **Decision tables** for *selection* (which rate card, which GL account, which tax code, which pay
  basis) — declarative, diffable, coverage-checkable, non-engineer-authorable.
- **Bounded expression DSL** for *arithmetic/conditions* — a tiny typed AST over a curated Fact
  Catalog, whitelisted ops/functions, decimal-only, no I/O, no loops, bounded depth. Pure and
  deterministic → replayable. Validated at author-time (schema + type + reference + completeness +
  dry-run) so a malformed rule can never reach pricing. Every evaluation writes an explainability
  trace (which rule row, input facts, output) onto the priced record — the SOX-lite "why did this
  line get GL 4000?" answer.

## The six layers, on the envelope

| Layer | What's configurable | Plugs in via |
|---|---|---|
| **Rating** | rate programs → components (per-mile/flat/%/tiered/zone/weight/spot/subscription/accessorial/detention), conditions, minimums, fuel | `rating_programs` reads `rate_cards`; emits the same `job_charges` shape → invoice chain untouched. Backfill shim from today's flat cards. |
| **Rev-rec / accounting method** | cash vs accrual; recognize on-invoice/at-delivery/over-time/milestone/ratable; fiscal calendar; FX | a **revenue sub-ledger** (`revenue_recognition_entries`) derived *beside* `issued_invoices`; deferred-revenue schedules; immutable period-close. Phase 0 = today's numbers exactly. |
| **Billing** | cycle, consolidation (per-load/period/lane/contract), numbering, terms, memos, disputes, write-offs, dunning, multi-currency | `billing_profiles` + a consolidation engine over `job_charges.billing_status`; credit/debit memos feed ZATCA unchanged. Seeded default = today's per-job flow. |
| **Settlement / AP** | pay programs → components + conditions, deductions catalog + advance/escrow **ledgers**, factoring/quick-pay, holdbacks, tax-form mapping (1099/T4A/none) | mirrors the AR chain; computes pay from **load + pay program, never `job_charges`**; RLS-enrolled, immutable, approval-gated. |
| **Integration (GL/tax/payments)** | which QBO/Xero/NetSuite, Avalara/TaxJar/ZATCA, Stripe/Bill.com; `account_mappings` | pluggable `IAccountingConnector`/`ITaxProvider`/`IPaymentProvider` reusing the existing connector + outbox/inbox + RLS infra; `gl_posting_log` prevents double-posting; ZATCA rehomed as one tax adapter. |
| **Config meta** | the envelope, resolver, DSL, templates, change-control | `fin_config_sets` + archetype templates. |

## Isolation, immutability, audit (uniform)
Every table is `company_id`-scoped and enrolled in the existing `tenant_isolation`/`platform_admin_bypass`
RLS via the stage22 reconcile pattern (+ `*SchemaService.EnsureAsync` for owner/dev). Issued invoices,
recognition entries, posted settlements, and published config versions are **append-only**; corrections
are reversing entries, never edits. Money-out and config-publish route through the existing approval
workflow. Every mutation emits a correlated domain event; `fin_config_change_log` + `audit_logs` give
point-in-time reconstruction ("what was tenant T's config on 2026-03-14, who approved it?").

## Archetype templates (productive on day one, tune from a baseline)
Platform-seed config sets cloned into a tenant at provisioning — **Spot broker**, **Contract carrier**,
**Subscription 3PL**, **Owner-operator** — each pre-wiring rating, billing, pay, rev-rec, tax, payment.
This is the guard against the "config so generic it's unusable" failure mode; the zero-tenant-branch
resolver is the guard against the "per-customer code fork" failure mode.

## Phased delivery (each phase: additive migration + ensure + RLS reconcile + feature-flag + `*PostgresTests` CI gate)
- **P0 — Seam migrations, no behavior change:** `job_charges.source/rate_basis/rated_at` + partial
  unique index; `config_set_id` pin columns (nullable); revenue sub-ledger with default profile =
  today's numbers. Reconciliation-invariant test.
- **P1 — Config envelope + resolver + change-control + archetype templates** (scaffolding, no behavior
  change). Unblocks everything.
- **P2 — Rating on config** (the #1 revenue leak): `rating_programs` + DSL interpreter + validator +
  dry-run; `POST /api/jobs/{id}/rate`; backfill shim reproduces legacy card numbers exactly.
- **P3 — Billing + tax + GL mapping:** POD-gated issuance (flag), `billing_profiles` + consolidation,
  `tax_profiles` (fills the hardcoded-zero tax hole), `gl_mapping_rules` + first accounting connector
  (QuickBooks) + Stripe AR + `gl_posting_log`.
- **P4 — Settlement/AP + accounting calendars + rev-rec methods + more connectors/factoring.**

## Non-negotiables (carried from every specialist)
Fail-closed (no config → no invented price/pay, existing leakage signal stands) · tenant resolved
server-side · money-math is **CI-integration-tested (`*PostgresTests`, live Postgres) before production
trust** · backward-compatible/additive (a seeded default reproduces today) · ZATCA hash chain preserved
(consolidation before issue, memos as first-class ZATCA docs) · settlement pay never read from
`job_charges` · no double-posting to the GL.
