using System.IO;

namespace Opstrax.Tests;

public sealed class TenantRlsCoverageReadinessRegressionTests
{
    [Fact]
    public void Stage53ReconcilesEveryBigintTenantTableWithAuditedExceptions()
    {
        var migration = Read("database", "migrations", "2026_07_30_stage53_tenant_rls_reconciliation.sql");

        Assert.Contains("c.column_name IN ('company_id','tenant_id')", migration);
        Assert.Contains("c.data_type='bigint'", migration);
        Assert.Contains("NOT IN ('platform_invoices','gps_gateway_replay')", migration);
        Assert.Contains("rec.table_name='companies' THEN 'id'", migration);
        Assert.Contains("('companies',true,true,true)", migration);
        Assert.Contains("('workforce_schedules',true,true,false)", migration);
        Assert.Contains("ENABLE ROW LEVEL SECURITY", migration);
        Assert.Contains("FORCE ROW LEVEL SECURITY", migration);
        Assert.Contains("SELECT policyname FROM pg_policies", migration);
        Assert.Contains("DROP POLICY %I ON public.%I", migration);
        Assert.Contains("COUNT(*) FROM pg_policies", migration);
        Assert.DoesNotContain("GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.%I", migration);
        Assert.Contains("('mfa_login_challenge_consumptions',true,false,true)", migration);
        Assert.Contains("('authorization_decision_logs',true,false,false)", migration);
        Assert.Contains("Some matrix members belong to optional non-Fleet packs", migration);
        Assert.Contains("AS matrix(table_name,allow_insert,allow_update,allow_delete)", migration);
        Assert.Contains("REVOKE ALL PRIVILEGES ON TABLE", migration);
        Assert.Contains("('security_events',true,false,false)", migration);
        Assert.Contains("('fleet_tms_branch_migration_audit',false,false,false)", migration);
        Assert.Contains("Stage-50's audited read-only contract", migration);
        Assert.Contains("REVOKE ALL PRIVILEGES ON TABLE public.%I", migration);
        Assert.Contains("GRANT SELECT ON TABLE public.%I", migration);
        Assert.Contains("reference-table sequence privilege verification failed", migration);
        Assert.Contains("ALTER DEFAULT PRIVILEGES FOR ROLE %I", migration);
        Assert.Contains("REVOKE ALL PRIVILEGES ON TABLES", migration);
        Assert.Contains("REVOKE ALL PRIVILEGES ON SEQUENCES", migration);
        Assert.Contains("broad opstrax_app default privileges remain", migration);
        Assert.Contains("REVOKE TRUNCATE,REFERENCES,TRIGGER", migration);
        Assert.Contains("tenant sequence privileges do not match insert capability", migration);
        Assert.Contains("REVOKE TEMPORARY ON DATABASE %I FROM PUBLIC", migration);
        Assert.Contains("has_database_privilege('opstrax_app',current_database(),'TEMPORARY')", migration);
        Assert.Contains("has_schema_privilege('opstrax_app','public','CREATE')", migration);
        foreach (var tuple in new[]
        {
            "('audit_logs',true,false,false)", "('fleet_tms_shipment_events',true,false,false)",
            "('fleet_tms_cold_chain_event_log',true,false,false)", "('fleet_tms_asset_events',true,false,false)",
            "('fleet_tms_barcode_scan_events',true,false,false)", "('fleet_tms_rfid_events',true,false,false)",
            "('compliance_expiry_events',true,true,false)", "('market_pack_branch_migration_audit',false,false,false)"
        }) Assert.Contains(tuple, migration);
    }

    [Fact]
    public void ProductionReadinessDynamicallyFailsOnTenantCoverageDriftAndRequiresStage53()
    {
        var source = Read("backend-dotnet", "Services", "FleetProductionReadinessService.cs");

        Assert.Contains("tenant_scope AS", source);
        Assert.Contains("tenant_coverage_violations", source);
        Assert.Contains("tenant_grant_violations", source);
        Assert.Contains("default_privilege_violations", source);
        Assert.Contains("COUNT(*) FROM pg_policies", source);
        Assert.Contains("has_sequence_privilege('opstrax_app',seq.oid,'UPDATE')", source);
        Assert.Contains("defaults.defaclobjtype IN ('r','S')", source);
        Assert.Contains("has_table_privilege('opstrax_app',scope.oid,'TRUNCATE')", source);
        Assert.Contains("has_database_privilege(rolname,current_database(),'TEMPORARY')", source);
        Assert.Contains("2026_07_30_stage53_tenant_rls_reconciliation", source);
        Assert.Contains("platform_invoices", source);
        Assert.Contains("gps_gateway_replay", source);
        Assert.Contains("WHEN c.relname='companies' THEN 'id'", source);
        Assert.Contains("workforce_contract_violations", source);
        Assert.Contains("2026_07_30_stage57_workforce_schedule_tenant_integrity", source);
        Assert.Contains("QuerySingleInSystemScopeAsync(Sql", source);
    }

    [Fact]
    public void RuntimeRlsReconcilerNeverWidensDatabasePrivileges()
    {
        var source = Read("backend-dotnet", "Services", "RlsReconciliationSchemaService.cs");

        Assert.DoesNotContain("GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES", source);
        Assert.DoesNotContain("GRANT USAGE, SELECT ON ALL SEQUENCES", source);
        Assert.Contains("Privileges are intentionally migration-owned", source);
        Assert.Contains("SELECT policyname FROM pg_policies", source);
        Assert.Contains("DROP POLICY %I ON public.%I", source);
        Assert.DoesNotContain("'platform_invoices', 'companies'", source);
        Assert.Contains("rec.table_name = 'companies' THEN 'id'", source);
        Assert.DoesNotContain("IF NOT EXISTS (\n                    SELECT 1 FROM pg_policies", source);
    }

    [Fact]
    public void PredeployRunnerOrdersAndPostchecksStage53()
    {
        var script = Read("tools", "apply-neon-predeploy-migrations.sh");

        Assert.True(script.IndexOf("2026_07_30_stage52_fleet_identity_uniqueness", StringComparison.Ordinal)
                    < script.IndexOf("2026_07_30_stage53_tenant_rls_reconciliation", StringComparison.Ordinal));
        Assert.True(script.IndexOf("2026_07_30_stage56_asset_type_integrity", StringComparison.Ordinal)
                    < script.IndexOf("2026_07_30_stage57_workforce_schedule_tenant_integrity", StringComparison.Ordinal));
        Assert.Contains("Stage-53 tenant RLS reconciliation ledger", script);
        Assert.Contains("COUNT(*) FROM pg_policies", script);
        Assert.Contains("ledgered reconciliation — reapplying to repair drift", script);
        Assert.Contains("Tenant sequence privileges do not match insert capability", script);
        Assert.Contains("Tenant RLS coverage:", script);
        Assert.Contains("WHEN cls.relname='companies' THEN 'id'", script);
        Assert.Contains("Fleet Stage54/55/56/57 migration ledger", script);
        Assert.Contains("workforce ownership indexes", script);
    }

    [Fact]
    public void TelemetrySigningKeyConsumesTheDeploymentConfigurationName()
    {
        var source = Read("backend-dotnet", "TelemetryKeyStore.cs");
        Assert.Contains("Environment.GetEnvironmentVariable(\"Sse__TicketKey\")", source);
        Assert.Contains("Environment.GetEnvironmentVariable(\"Telemetry__SseTicketKey\")", source);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()));
    }
}
