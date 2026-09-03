# G7B — Fleet Intelligence & Governed Automation Execution

Issue: #154  
Entry baseline: `main@6674f52f5fb8902af0cb777f2e0a893a14173b4b`

## Current-build baseline
- Descriptive analytics, telemetry provenance and AI recommendation scaffolding exist.
- Product audit records no real predictive maintenance, forecasting or anomaly-detection model serving layer today.
- Existing rule engines and seeded/demo recommendations must not be relabelled as ML evidence.

## First implementation slices
1. Model-truth contract tests: UI/API may not say model active/predictive without a registered serving model and evaluation record.
2. Canonical feature-quality contract: source event, observed time, freshness, quality flags, missing/unknown semantics.
3. Model registry/evaluation schema design and migration proposal; migration waits for schema-authority slot.
4. Baseline evaluators for predictive-maintenance and ETA tasks using historical labelled data where available; compare against trivial/rule baselines.
5. Copilot grounding contract: tenant-scoped source references, fact/inference/recommendation distinction and recommendation identity.
6. Human-approved automation contract with preview/idempotency/audit and no direct unsafe command autonomy.

## Conflict domain
- New model registry/evaluation persistence needs serialized schema authority.
- Existing AI recommendation/agentic-ops code may be touched only after file ownership is reconciled.
- Evaluation/test harness/documentation can proceed independently.

## Acceptance truth
No predictive/AI capability promotion from scaffolding, prompt output or unit tests alone. Model-specific evidence and independent AI/Data/Product/Safety/Security/SDET acceptance remain mandatory.