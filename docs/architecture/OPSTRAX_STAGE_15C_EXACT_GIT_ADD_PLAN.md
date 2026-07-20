# Stage 15C Exact Git Add Plan

## Backend source

```bash
git add backend-dotnet/Controllers/EndpointMappings.cs
git add backend-dotnet/Controllers/PlatformEndpoints.cs
git add backend-dotnet/Program.cs
git add backend-dotnet/Services/Batch3SchemaService.cs
git add backend-dotnet/Services/Batch4SchemaService.cs
git add backend-dotnet/Services/MaintenanceBackgroundService.cs
git add backend-dotnet/Services/SafetyBackgroundService.cs
git add backend-dotnet/Services/TelemetryBackgroundService.cs
git add backend-dotnet/Services/TelemetrySchemaService.cs
git add backend-dotnet/Controllers/SafetyMaintenanceFoundationEndpoints.cs
git add backend-dotnet/Controllers/Stage9Endpoints.cs
git add backend-dotnet/Controllers/BusinessSpineEndpoints.cs
git add backend-dotnet/Controllers/RevenueReadinessEndpoints.cs
git add backend-dotnet/Services/OutboxDispatcherBackgroundService.cs
git add backend-dotnet/Services/Stage9OperationalFoundationService.cs
git add backend-dotnet/Services/Stage9SchemaService.cs
git add backend-dotnet/Services/SafetyMaintenanceFoundationService.cs
git add backend-dotnet/Services/SafetyMaintenanceFoundationSchemaService.cs
git add backend-dotnet/Services/FoundationSchemaService.cs
git add backend-dotnet/Foundation/FoundationContracts.cs
git add backend-dotnet/Foundation/FoundationServices.cs
git add backend-dotnet/Foundation/FoundationPersistenceServices.cs
```

## Frontend source

```bash
git add frontend/src/App.tsx
git add frontend/src/layouts/AppShell.tsx
git add frontend/src/layouts/PlatformShell.tsx
git add frontend/src/modules/moduleConfig.ts
git add frontend/src/pages/AdminPage.tsx
git add frontend/src/pages/CommandCenterPage.tsx
git add frontend/src/pages/FeatureFlagsPage.tsx
git add frontend/src/pages/FinancialAnalyticsPage.tsx
git add frontend/src/pages/LiveMapPage.tsx
git add frontend/src/pages/platform/PlatformApp.tsx
git add frontend/src/pages/platform/PlatformCommandCenterPage.tsx
git add frontend/src/pages/TripsPage.tsx
git add frontend/src/pages/OperationsProofCenterPage.tsx
git add frontend/src/pages/platform/PlatformCommercialOpsPage.tsx
git add frontend/src/services/adminApi.ts
git add frontend/src/services/fleetHealthApi.ts
git add frontend/src/services/fuelApi.ts
git add frontend/src/services/incidentsApi.ts
git add frontend/src/services/maintenanceApi.ts
git add frontend/src/services/platformApi.ts
git add frontend/src/services/safetyApi.ts
git add frontend/src/services/operationsProofApi.ts
git add frontend/src/services/telemetryApi.ts
git add frontend/src/auth/rbacConfig.ts
```

## Tests

```bash
git add backend-dotnet.Tests/BusinessSpinePostgresTests.cs
git add backend-dotnet.Tests/FoundationDispatcherPostgresTests.cs
git add backend-dotnet.Tests/FoundationPostgresSmokeTests.cs
git add backend-dotnet.Tests/FoundationTests.cs
git add backend-dotnet.Tests/PlatformCommercialOpsTests.cs
git add backend-dotnet.Tests/RevenueReadinessPostgresTests.cs
git add backend-dotnet.Tests/Stage10PostgresTests.cs
git add backend-dotnet.Tests/Stage12TelemetryTests.cs
git add backend-dotnet.Tests/Stage13BSafetyMaintenanceTests.cs
git add backend-dotnet.Tests/Stage13SourceRegressionTests.cs
git add backend-dotnet.Tests/Stage15SourceRegressionTests.cs
git add backend-dotnet.Tests/Stage9PostgresTests.cs
```

## Database migrations

```bash
git add database/migrations/2026_06_27_stage5_p0b1a_foundation.sql
git add database/migrations/2026_06_28_stage5b_p0b1a2_persistence_hardening.sql
git add database/migrations/2026_06_28_stage5d_p0b1a3_dispatcher.sql
git add database/migrations/2026_06_28_stage6_p0b1b_business_spine.sql
git add database/migrations/2026_06_28_stage7a_revenue_readiness_schema_contract.sql
git add database/migrations/2026_06_28_stage8_finance_activation.sql
git add database/migrations/2026_06_28_stage12a_telemetry_live_state.sql
git add database/migrations/2026_06_28_stage13b_safety_maintenance_foundation.sql
```

## Docs

```bash
git add docs/ARCHITECTURE.md
git add docs/PRODUCT_MODULES.md
git add docs/architecture/OPSTRAX_STAGE_15B_WORKTREE_SNAPSHOT.md
git add docs/architecture/OPSTRAX_STAGE_15B_STAGE_CLAIM_VERIFICATION.md
git add docs/architecture/OPSTRAX_STAGE_15B_ROUTE_NAV_VERIFICATION.md
git add docs/architecture/OPSTRAX_STAGE_15B_API_CLIENT_ENDPOINT_VERIFICATION.md
git add docs/architecture/OPSTRAX_STAGE_15B_FAKE_FALLBACK_VERIFICATION.md
git add docs/architecture/OPSTRAX_STAGE_15B_SECRET_CONFIG_SCAN.md
git add docs/architecture/OPSTRAX_STAGE_15B_MIGRATION_REVIEW.md
git add docs/architecture/OPSTRAX_STAGE_15B_PACKAGE_LOCK_REVIEW.md
git add docs/architecture/OPSTRAX_STAGE_15B_GENERATED_ARTIFACT_REVIEW.md
git add docs/architecture/OPSTRAX_STAGE_15B_RBAC_TENANT_CUSTOMER_VERIFICATION.md
git add docs/architecture/OPSTRAX_STAGE_15B_AI_GOVERNANCE_VERIFICATION.md
git add docs/architecture/OPSTRAX_STAGE_15B_BUILD_TEST_LINT_RESULTS.md
git add docs/architecture/OPSTRAX_STAGE_15B_COMMIT_GROUPING_PLAN.md
git add docs/architecture/OPSTRAX_STAGE_15B_ROLLBACK_PLAN.md
git add docs/architecture/OPSTRAX_STAGE_15B_GIT_PUSH_GUIDANCE.md
git add docs/architecture/OPSTRAX_STAGE_15B_FINAL_DELIVERY_ASSURANCE_REVIEW.md
git add docs/architecture/OPSTRAX_STAGE_15B_COMPLETION_REPORT.md
git add docs/architecture/OPSTRAX_STAGE_15C_WORKTREE_INVENTORY.md
git add docs/architecture/OPSTRAX_STAGE_15C_SECRET_STAGING_REVIEW.md
git add docs/architecture/OPSTRAX_STAGE_15C_GENERATED_EXCLUSION_REVIEW.md
git add docs/architecture/OPSTRAX_STAGE_15C_GITIGNORE_REVIEW.md
git add docs/architecture/OPSTRAX_STAGE_15C_SAFE_STAGING_MANIFEST.md
git add docs/architecture/OPSTRAX_STAGE_15C_EXACT_GIT_ADD_PLAN.md
git add docs/architecture/OPSTRAX_STAGE_15C_PRE_COMMIT_VERIFICATION_PLAN.md
git add docs/architecture/OPSTRAX_STAGE_15C_COMMIT_MESSAGE_PLAN.md
git add docs/architecture/OPSTRAX_STAGE_15C_PUSH_READINESS_DECISION.md
```

## Hygiene / config

```bash
git add .gitignore
```

## Verification after staging

```bash
git status --short
git diff --cached --stat
git diff --cached --check
```

