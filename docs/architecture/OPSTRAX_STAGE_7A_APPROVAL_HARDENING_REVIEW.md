# Stage 7A Approval Hardening Review

| Action | Current Behavior | Required Behavior | Gap | Stage 8 Decision |
|---|---|---|---|---|
| Invoice approval | Not yet a distinct workflow. | Must require approval before invoice issue. | High | Block invoice issue until Stage 8 adds this. |
| Invoice issue conversion | Not implemented. | Must convert approved drafts to issued invoices. | High | Stage 8 should implement. |
| Credit note approval | Not implemented. | Must require approval. | High | Stage 8 planning only. |
| Payment adjustment | Not implemented. | Must require approval. | High | Stage 8 planning only. |
| Invoice void/cancel | Not implemented. | Must require approval and audit. | Medium | Stage 8 planning only. |
| Rate card change on active contract | Approval-gated. | Must remain approval-gated. | Low | Keep as-is. |
| External customer invoice send | Not implemented. | Must require approval and audit. | High | Stage 8 should include this guardrail. |

## Verdict

The approval foundation is sufficient for the next finance activation slice, but invoice issuance and downstream finance actions remain blocked until Stage 8 adds explicit approval flows.
