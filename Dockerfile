FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend-dotnet/Opstrax.Api.csproj backend-dotnet/
RUN dotnet restore backend-dotnet/Opstrax.Api.csproj
COPY backend-dotnet/ backend-dotnet/
COPY database/init/001_schema.sql database/init/001_schema.sql
COPY database/migrations/2026_07_30_customer_feedback_contract.sql database/migrations/
COPY database/migrations/2026_07_30_stage49_mfa_challenge_one_time.sql database/migrations/
COPY database/migrations/2026_07_30_stage50_fleet_production_contract.sql database/migrations/
COPY database/migrations/2026_07_30_stage51_production_runtime_support.sql database/migrations/
COPY database/migrations/2026_07_30_stage52_fleet_identity_uniqueness.sql database/migrations/
COPY database/migrations/2026_07_30_stage53_tenant_rls_reconciliation.sql database/migrations/
COPY database/migrations/2026_07_30_stage54_cold_chain_device_integrity.sql database/migrations/
COPY database/migrations/2026_07_30_stage55_fleet_runtime_route_contract.sql database/migrations/
COPY database/migrations/2026_07_30_stage56_asset_type_integrity.sql database/migrations/
COPY database/migrations/2026_07_30_stage57_workforce_schedule_tenant_integrity.sql database/migrations/
COPY database/migrations/2026_07_31_stage58_nonforgeable_tenant_ticket.sql database/migrations/
COPY database/migrations/2026_07_31_stage59_data_protection_key_ring.sql database/migrations/
COPY database/migrations/2026_08_01_stage60_dispatch_trip_pilot.sql database/migrations/
COPY database/migrations/2026_08_01_stage61_operations_proof_center.sql database/migrations/
COPY database/migrations/2026_08_01_stage62_last_mile_pilot.sql database/migrations/
COPY database/migrations/2026_08_01_stage63_route_plans_pilot.sql database/migrations/
RUN dotnet publish backend-dotnet/Opstrax.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/database/migrations ./Migrations
EXPOSE 10000
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
USER $APP_UID
ENTRYPOINT ["dotnet", "Opstrax.Api.dll"]
