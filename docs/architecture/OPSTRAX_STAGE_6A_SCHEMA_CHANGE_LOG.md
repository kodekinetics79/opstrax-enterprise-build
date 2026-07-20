# Stage 6A Schema Change Log

## Local additive schema work

- `jobs.rate_card_id` added as a nullable bridge column.
- `trips.trip_number` added as a nullable bridge column.
- `trip_stops.address_id` added as a nullable bridge column.
- Supporting indexes were added for the above bridge columns.

## Canonical stage tables already present

- `business_surface_profiles`
- `rate_cards`
- `job_charges`

## Legacy schema references

- `customers`
- `customer_contacts`
- `customer_addresses`
- `contracts`
- `contract_rates`
- `jobs`
- `trips`
- `trip_stops`

## Migration discipline

- All Stage 6A work is additive.
- No destructive DDL was introduced.
- No production migration was run.
- The bridge code now tolerates the reduced local DB shape where legacy business tables are absent.

