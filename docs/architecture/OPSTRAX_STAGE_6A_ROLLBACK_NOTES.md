# Stage 6A Rollback Notes

## No destructive change was applied

- No production migration was run.
- No table drop or data delete migration was introduced.
- The local work only added additive bridge logic and documentation.

## If the local bridge needs to be reverted

- Remove the new bridge endpoints from `backend-dotnet/Controllers/BusinessSpineEndpoints.cs`.
- Remove the canonical permission aliases and event publications from `backend-dotnet/Controllers/EndpointMappings.cs`.
- Remove the additional mirror/update helpers from `backend-dotnet/Services/BusinessSpineServices.cs`.
- Re-run `dotnet build` and `dotnet test`.

## If additive bridge columns need to be rolled back locally

- `ALTER TABLE jobs DROP COLUMN IF EXISTS rate_card_id;`
- `ALTER TABLE trips DROP COLUMN IF EXISTS trip_number;`
- `ALTER TABLE trip_stops DROP COLUMN IF EXISTS address_id;`

Those rollback commands are local-only and should not be executed against production.

