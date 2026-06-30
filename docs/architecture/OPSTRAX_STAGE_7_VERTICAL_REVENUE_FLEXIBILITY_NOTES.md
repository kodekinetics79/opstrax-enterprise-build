# Stage 7 Vertical Revenue Flexibility Notes

## Why this slice is vertical-agnostic

- Customer, contract, job, charge, and invoice draft rows are all tenant-scoped.
- Pricing fields already allow different commercial models:
  - `billing_basis`
  - `service_scope`
  - `vehicle_type`
  - `accessorial_type`
  - `currency`
  - `minimum_charge`
  - `fuel_surcharge_percent`

## Supported models

- Flat-rate service work
- Mileage-based charges
- Contract-backed recurring service
- Accessorial-heavy freight or field-service pricing
- Draft invoicing with approval gating

## Design implication

- The stage does not hardcode a single vertical.
- It provides enough structure for logistics, field service, and similar revenue patterns to share the same foundation.
