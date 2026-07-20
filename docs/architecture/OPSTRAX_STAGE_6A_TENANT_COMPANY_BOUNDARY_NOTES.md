# Stage 6A Tenant / Company Boundary Notes

`company_id` remains the effective tenant boundary for this repo.

## What was verified

- The foundation auth pipeline fails closed when tenant context is missing.
- The new business-spine bridge uses `company_id` in create/update/filter paths.
- The legacy business schema in `database/init/001_schema.sql` also uses `company_id` on customers, contracts, jobs, trips, contacts, addresses, and charges.
- The stage 6 tables `rate_cards` and `job_charges` are also tenant-scoped.

## What was not changed

- No tenant field rename was attempted.
- No cross-tenant relaxation was added.
- No production connection string or production migration was touched.

## Practical boundary rule

- Use `company_id` for tenant-owned records.
- Deny or fail closed when `company_id` is missing.
- Do not infer tenant from payload fields alone.
- Carry `correlation_id` and `causation_id` where available, but never use them as tenant identity.

