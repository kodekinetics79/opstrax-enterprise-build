# Stage 7A Schema Contract Review

## Result

The Stage 7 revenue schema is now represented by a formal SQL contract:

- [`database/migrations/2026_06_28_stage7a_revenue_readiness_schema_contract.sql`](/Users/zackkhan/Downloads/opstrax-enterprise-build-fixed-nginx/database/migrations/2026_06_28_stage7a_revenue_readiness_schema_contract.sql)

## Contract contents

- `invoice_drafts`
- `invoice_draft_lines`
- `company_id`-scoped uniqueness and lookup indexes
- foreign keys where safe
- `created_at` / `updated_at` timestamps
- `jsonb` metadata support

## Match to startup service

- The SQL contract matches the runtime schema service shape.
- The only difference is operational: startup service still bootstraps local/dev databases, while the SQL file is the reviewable enterprise contract.

## Safety

- The contract is additive.
- No destructive statements were introduced.
- No production migration was applied.
