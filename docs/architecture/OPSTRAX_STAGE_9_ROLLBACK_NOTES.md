# Stage 9 Rollback Notes

## Safe rollback posture

Stage 9 is additive. A local rollback can be done by removing the new Stage 9 tables and the new route/service registrations, or by resetting the local database if the environment is disposable.

## What to remove first

1. Stage 9 route mappings in `backend-dotnet/Controllers/EndpointMappings.cs`
2. `backend-dotnet/Controllers/Stage9Endpoints.cs`
3. `backend-dotnet/Services/Stage9OperationalFoundationService.cs`
4. `backend-dotnet/Services/Stage9SchemaService.cs`
5. Stage 9 registration lines in `backend-dotnet/Program.cs`

## Schema rollback guidance

- Remove the Stage 9 tables if a local database reset is needed:
  - `smart_assignment_recommendations`
  - `assignment_confirmations`
  - `site_access_requirements`
  - `access_documents`
  - `pickup_authorizations`
  - `warehouse_handovers`
  - `proof_packages`
  - `proof_artifacts`
  - `billing_confidence_records`
- Do not run destructive rollback SQL against production.

## Risk note

- Rolling back this slice removes mobile-ready operational proof storage, so do it only in disposable local environments.

