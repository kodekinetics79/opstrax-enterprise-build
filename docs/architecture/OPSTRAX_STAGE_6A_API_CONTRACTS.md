# Stage 6A API Contracts

## Canonical business bridge

- `GET /api/business/profile`
- `PUT /api/business/profile`
- `GET /api/rate-cards`
- `POST /api/rate-cards`
- `PATCH /api/rate-cards/{id}`
- `GET /api/job-charges`
- `POST /api/job-charges`
- `PATCH /api/job-charges/{id}`

## Legacy compatibility surface

- `GET /api/customers`
- `GET /api/customers/{id}`
- `POST /api/customers`
- `PUT /api/customers/{id}`
- `GET /api/contracts`
- `GET /api/contracts/{id}`
- `POST /api/contracts`
- `PUT /api/contracts/{id}`
- `GET /api/contracts/{id}/rates`
- `POST /api/contracts/{id}/rates`
- `PUT /api/contracts/{id}/rates/{rateId}`
- `GET /api/jobs`
- `GET /api/jobs/{id}`
- `POST /api/jobs`
- `PUT /api/jobs/{id}`
- `GET /api/trips`
- `GET /api/trips/{id}`

## Permission model

- `customer.account.read/create/update`
- `contract.read/create/update`
- `rate_card.read/create/update`
- `job.read/create/update`
- `trip.read/create/update`
- `charge.read/create/update`

Legacy permission keys remain accepted through aliasing:

- `customers:view`
- `customers:create`
- `customers:update`
- `finance:manage`
- `dispatch:manage`

## Event contract

- Customer, contract, job, and rate-card bridge writes now publish domain events through the foundation publisher.
- Canonical event names used here are additive and tenant-scoped.

