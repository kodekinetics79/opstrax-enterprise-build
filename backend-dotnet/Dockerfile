FROM mcr.microsoft.com/dotnet/sdk:8.0.419-bookworm-slim@sha256:dd09bcce84d9130e7f3e85c83a8ce9709e1d95e45c48c722a0d7923b38d8024c AS build
WORKDIR /src
COPY backend-dotnet/Opstrax.Api.csproj backend-dotnet/
COPY telematics/src/Opstrax.Telematics.Protocols.J1939/Opstrax.Telematics.Protocols.J1939.csproj telematics/src/Opstrax.Telematics.Protocols.J1939/
RUN dotnet restore backend-dotnet/Opstrax.Api.csproj
COPY backend-dotnet/ backend-dotnet/
COPY telematics/src/Opstrax.Telematics.Protocols.J1939/ telematics/src/Opstrax.Telematics.Protocols.J1939/
COPY database/init/001_schema.sql database/init/001_schema.sql
COPY database/migrations/2026_07_11_stage32_device_imei.sql database/migrations/
COPY database/migrations/2026_07_16_stage42_telemetry_gateways.sql database/migrations/
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
COPY database/migrations/2026_08_01_stage64_shipments_pilot.sql database/migrations/
COPY database/migrations/2026_07_22_stage47_detention_recovery.sql database/migrations/
COPY database/migrations/2026_08_01_stage65_safety_pilot.sql database/migrations/
COPY database/migrations/2026_08_02_stage66_telematics_pilot.sql database/migrations/
COPY database/migrations/2026_08_02_stage67_telematics_diagnostics_integrity.sql database/migrations/
COPY database/migrations/2026_08_02_stage68_entitlement_policy_mode.sql database/migrations/
COPY database/migrations/2026_08_02_stage69_market_pack_control_hardening.sql database/migrations/
COPY database/migrations/2026_08_02_stage70_hos_pilot_schema_reconciliation.sql database/migrations/
COPY database/migrations/2026_08_02_stage71_coaching_evidence_reconciliation.sql database/migrations/
COPY database/migrations/2026_08_02_stage72_hos_offboarding_immutability_reconciliation.sql database/migrations/
COPY database/migrations/2026_08_02_stage73_hos_offboarding_null_fail_closed.sql database/migrations/
COPY database/migrations/2026_08_02_stage74_retention_policy_production_contract.sql database/migrations/
COPY database/migrations/2026_08_02_stage75_bounded_support_access.sql database/migrations/
COPY database/migrations/2026_08_11_stage76_telematics_security_hardening.sql database/migrations/
COPY database/migrations/2026_08_12_stage77_protected_role_bootstrap.sql database/migrations/
COPY database/migrations/2026_08_13_stage78_country_profiles_runtime_contract.sql database/migrations/
COPY database/migrations/2026_08_13_stage79_tenant_provisioning_runtime_contract.sql database/migrations/
COPY database/migrations/2026_08_13_stage80_driver_proof_upload_binding.sql database/migrations/
COPY database/migrations/2026_08_14_stage80_fleet_identity_backbone.sql database/migrations/
COPY database/migrations/2026_08_18_stage81_platform_billing_itemization.sql database/migrations/
COPY database/migrations/2026_08_20_stage81_customer_eta_secure_token.sql database/migrations/
COPY database/migrations/2026_08_20_stage82_telematics_device_credential_constraint.sql database/migrations/
COPY database/migrations/telematics database/migrations/telematics
RUN dotnet publish backend-dotnet/Opstrax.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0.30-bookworm-slim@sha256:b0beb9cc1dee1c1b0749796110d4734292071b814207ad0d4f40611f7db04f7b AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/database/migrations ./Migrations
ENV Rls__EnforceTenantContext=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Opstrax.Api.dll"]
