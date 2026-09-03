# G7D — Migration, Vertical Packs & Enterprise Final Release Execution

Issue: #156  
Entry baseline: `main@6674f52f5fb8902af0cb777f2e0a893a14173b4b`

## Current-build baseline
- OpsTrax already has tenant provisioning, fleet data, import/export surfaces, market/country profiles, package/entitlement foundations, provider connectors and guarded load tooling.
- No universal incumbent-migration claim exists.
- Wave 6 already owns 1K–5K+ release resilience; G7D extends final supported enterprise tiers to 10K/25K/50K where evidence supports them.

## First implementation slices
1. Migration source contract: source type/version, customer authority, snapshot timestamp, checksums, record counts and immutable import-run identity.
2. Preview/mapping/reconciliation result model with imported/rejected/needs-review/duplicate counts and resumable retries.
3. Generic CSV/XLSX/JSON tenant/fleet migration harness before provider-specific adapters.
4. Provider-specific import adapters only for authorized official APIs/customer exports; no scraping.
5. Vertical-pack definition format built on existing package/market-profile primitives instead of code forks.
6. Enterprise-scale launch profiles extending existing guarded k6 tooling to 10K/25K/50K only in isolated authorized environments.
7. Final release evidence index mapping every sold capability to its owning gate and truth status.

## Conflict domain
- Migration persistence may require serialized schema authority.
- Package/entitlement edits coordinate with G6B.
- Scale profiles coordinate with G6A but can be authored independently.

## Acceptance truth
G7D is the final program gate but cannot waive prior evidence. If an earlier capability remains PILOT/DEVELOPMENT/ROADMAP, the final package must exclude it or state the permitted limitation explicitly.