# OpsTrax Data Classification and Retention

## Data Classes
- Public
- Internal
- Confidential
- Restricted
- Regulated/Sensitive

## Examples
- Driver safety video: Restricted
- Customer invoices: Confidential
- AI memory: Restricted
- Evidence packs: Restricted
- Platform billing: Confidential
- Vehicle locations: Regulated/Sensitive

## Retention Recommendations

| Data Type | Suggested Retention | Notes |
|---|---|---|
| Telemetry / location | Short-to-medium retention with rollups | Keep raw only as long as needed for operations and compliance |
| Video / media | Short retention unless evidence hold applies | Expensive and highly sensitive |
| Audit logs | Longer retention | Must support investigations and compliance |
| AI logs | Medium retention with masking | Keep enough for traceability and safety review |
| Invoices / payments | Long retention | Financial records require durable history |
| Customer contracts | Long retention | Commercial and legal record |
| Evidence packs | Long retention or legal hold | Treat as restricted artifacts |
| Driver documents | Long retention / regulatory schedule | Subject to policy and jurisdiction |
| Compliance records | Long retention | Audit and certification history |

