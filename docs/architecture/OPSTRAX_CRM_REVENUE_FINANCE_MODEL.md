# OpsTrax CRM / Revenue / Finance Model

## Commercial Spine
Lead -> Opportunity -> Quote -> Customer -> Contract -> Rate Card -> SLA -> Job -> Trip -> POD -> Job Charge -> Invoice -> Payment -> Margin -> Renewal

## What Must Be Covered
- Customer master and contacts
- Customer portal
- Lead and opportunity pipeline
- Quotes and approvals
- Contract versioning
- Rate cards and accessorial charges
- SLA rules and service credits
- Invoice generation and disputes
- AR aging
- Revenue recognition
- Job and trip cost capture
- Profitability and leakage detection
- Renewal tracking, churn risk, and upsell recommendations
- Campaigns and sales commissions

## Architecture Notes
- Revenue data must be tenant-scoped and auditable.
- Finance calculations should be server-side and deterministic.
- Revenue leakage should be triggered by missing charges, SLA misses, and unbilled trip costs.
- Margin leakage should compare expected versus actual job/trip cost.

## API / Service Expectations
- CRM lifecycle APIs for leads, opportunities, quotes, contracts, and renewals.
- Billing services for invoice creation, disputes, and credit notes.
- Margin and AR services for profitability and aging reports.

## Dashboard Expectations
- Pipeline conversion
- Renewal pipeline
- AR aging
- Revenue leakage
- Margin by customer, lane, vehicle, and job

## AI Opportunities
- Quote suggestions
- Renewal risk detection
- Upsell recommendations
- Leakage alerts

## Priority
- P0: customer master, contracts, invoices, margin basics
- P1: renewals, leakage, commissions, churn risk
- P2: advanced campaign automation

