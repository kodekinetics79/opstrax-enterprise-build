using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class DemoCertificationTruthContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void CanonicalDemoEldAndHosUiCannotImplyProviderOrProductCertification()
    {
        var seeder = File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Services", "DemoTenantSeeder.cs"));
        var hos = File.ReadAllText(Path.Combine(Root, "frontend", "src", "pages", "HosEldPage.tsx"));
        var driverHos = File.ReadAllText(Path.Combine(Root, "frontend", "src", "pages", "driver", "DriverHosPage.tsx"));
        var fleetCompliance = File.ReadAllText(Path.Combine(Root, "frontend", "src", "pages", "FleetCompliancePage.tsx"));
        var legacyDemoSeeds = File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Services", "Batch6SchemaService.cs"));
        var scaleHarness = File.ReadAllText(Path.Combine(Root, "database", "seeds", "acme_pilot_harness.sql"));

        Assert.Contains("'Synthetic demo ELD','Synthetic fixture — no provider account'", seeder, StringComparison.Ordinal);
        Assert.Contains("NULL,'demo-fixture','Unverified'", seeder, StringComparison.Ordinal);
        Assert.DoesNotContain("NOW()-INTERVAL '5 minute','pilot-1.0','Healthy'", seeder, StringComparison.Ordinal);
        Assert.Contains("Driver attestation", hos, StringComparison.Ordinal);
        Assert.Contains("It is not ELD product certification", hos, StringComparison.Ordinal);
        Assert.Contains("Driver attested", driverHos, StringComparison.Ordinal);
        Assert.Contains("Record attestation", driverHos, StringComparison.Ordinal);
        Assert.Contains("Synthetic demo gateway", seeder, StringComparison.Ordinal);
        Assert.Contains("DEMO-ELD-{companyId}", seeder, StringComparison.Ordinal);
        Assert.DoesNotContain("var providers = new[] { \"Geotab\", \"Samsara\", \"Motive\" }", seeder, StringComparison.Ordinal);
        Assert.DoesNotContain("Active devices must carry real credentials", seeder, StringComparison.Ordinal);
        Assert.Contains("Provider evidence", hos, StringComparison.Ordinal);
        Assert.Contains("ELD records", fleetCompliance, StringComparison.Ordinal);
        Assert.DoesNotContain("KpiCard label=\"ELD\" value={(hos?.eldDevices ?? []).length ? \"Registered\"", fleetCompliance, StringComparison.Ordinal);
        var legacyEldStart = legacyDemoSeeds.IndexOf("INSERT INTO eld_devices (id,device_serial", StringComparison.Ordinal);
        var legacyEldEnd = legacyDemoSeeds.IndexOf("ON CONFLICT DO NOTHING", legacyEldStart, StringComparison.Ordinal);
        Assert.True(legacyEldStart >= 0 && legacyEldEnd > legacyEldStart);
        Assert.DoesNotContain("'KeepTruckin M300'", legacyDemoSeeds[legacyEldStart..legacyEldEnd], StringComparison.Ordinal);
        Assert.DoesNotContain("'Samsara VG34'", legacyDemoSeeds[legacyEldStart..legacyEldEnd], StringComparison.Ordinal);
        Assert.DoesNotContain("'Omnitracs IVG'", legacyDemoSeeds[legacyEldStart..legacyEldEnd], StringComparison.Ordinal);
        var legacyHosStart = legacyDemoSeeds.IndexOf("INSERT INTO hos_logs (id,company_id", StringComparison.Ordinal);
        var legacyHosEnd = legacyDemoSeeds.IndexOf("ON CONFLICT DO NOTHING", legacyHosStart, StringComparison.Ordinal);
        Assert.True(legacyHosStart >= 0 && legacyHosEnd > legacyHosStart);
        Assert.DoesNotContain(",true)", legacyDemoSeeds[legacyHosStart..legacyHosEnd], StringComparison.Ordinal);
        Assert.Contains("Acme Transport — Demo", scaleHarness, StringComparison.Ordinal);
        Assert.DoesNotContain("ARRAY['Geotab','Samsara','Motive']", scaleHarness, StringComparison.Ordinal);
        Assert.Contains("Synthetic fixture — no provider account", scaleHarness, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage133ScrubsOnlyTheRecognizedDemoFixtureAndIsInTheProtectedRunner()
    {
        var migration = File.ReadAllText(Path.Combine(Root, "database", "migrations", "2026_09_09_stage133_demo_eld_certification_truth.sql"));
        var runner = File.ReadAllText(Path.Combine(Root, "tools", "apply-neon-predeploy-migrations.sh"));

        Assert.Contains("device_serial LIKE 'MER-ELD-%'", migration, StringComparison.Ordinal);
        Assert.Contains("LOWER(c.name) LIKE '%demo%'", migration, StringComparison.Ordinal);
        Assert.Contains("provider_sync_status = 'Unverified'", migration, StringComparison.Ordinal);
        Assert.Contains("last_sync_at = NULL", migration, StringComparison.Ordinal);
        Assert.Contains("device_serial ~ ('^ELD-' || d.company_id || '-[0-9]{3}$')", migration, StringComparison.Ordinal);
        Assert.Contains("Driver attestation of a complete daily HOS record", migration, StringComparison.Ordinal);
        Assert.Contains("company_code = 'ACME-TRANSPORT'", migration, StringComparison.Ordinal);
        Assert.Contains("ACME-DEMO-ELD-", migration, StringComparison.Ordinal);
        Assert.Contains("2026_09_09_stage133_demo_eld_certification_truth", runner, StringComparison.Ordinal);
        Assert.Contains("Stage133 demo ELD provider truth cleanup is incomplete", runner, StringComparison.Ordinal);
        Assert.Contains("Stage133 enriched demo gateway truth cleanup is incomplete", runner, StringComparison.Ordinal);
        Assert.Contains("Stage133 ACME demo harness truth cleanup is incomplete", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage134ReconcilesTheOriginalDemoTenantWithoutTouchingOtherTenants()
    {
        var migration = File.ReadAllText(Path.Combine(Root, "database", "migrations", "2026_09_09_stage134_legacy_demo_eld_reconciliation.sql"));
        var runner = File.ReadAllText(Path.Combine(Root, "tools", "apply-neon-predeploy-migrations.sh"));

        Assert.Contains("company_code='OPX-DEMO'", migration, StringComparison.Ordinal);
        Assert.Contains("'KeepTruckin M300','Samsara VG34','Omnitracs IVG','Synthetic demo ELD'", migration, StringComparison.Ordinal);
        Assert.Contains("device_serial ~ '^(DEMO-)?ELD-[0-9]{3}-(TRK|VAN|BOX)[0-9]{3}$'", migration, StringComparison.Ordinal);
        Assert.Contains("'DEMO-' || d.device_serial", migration, StringComparison.Ordinal);
        Assert.Contains("provider = 'Synthetic fixture — no provider account'", migration, StringComparison.Ordinal);
        Assert.Contains("provider_account_ref = NULL", migration, StringComparison.Ordinal);
        Assert.Contains("last_sync_at = NULL", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE companies", migration, StringComparison.Ordinal);
        Assert.Contains("2026_09_09_stage134_legacy_demo_eld_reconciliation", runner, StringComparison.Ordinal);
        Assert.Contains("Stage134 original OPX-DEMO ELD truth cleanup is incomplete", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage135RemovesOnlyExactDemoOperationalContradictions()
    {
        var migration = File.ReadAllText(Path.Combine(Root, "database", "migrations", "2026_09_10_stage135_demo_operational_truth_reconciliation.sql"));
        var runner = File.ReadAllText(Path.Combine(Root, "tools", "apply-neon-predeploy-migrations.sh"));
        var batch2 = File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Services", "Batch2SchemaService.cs"));
        var batch7 = File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Services", "Batch7SchemaService.cs"));

        Assert.Contains("LOWER(c.name) LIKE '%demo%' OR LOWER(c.company_code) LIKE '%demo%'", migration, StringComparison.Ordinal);
        Assert.Contains("LOWER(COALESCE(pod.proof_type,'')) = 'placeholder'", migration, StringComparison.Ordinal);
        Assert.Contains("COALESCE(pod.notes,'') = 'Batch 2 proof placeholder.'", migration, StringComparison.Ordinal);
        Assert.Contains("COALESCE(j.job_number,j.job_code) = 'JOB-1005'", migration, StringComparison.Ordinal);
        Assert.Contains("j.deleted_at IS NULL", migration, StringComparison.Ordinal);
        Assert.Contains("2026_09_10_stage135_demo_operational_truth_reconciliation", migration, StringComparison.Ordinal);
        Assert.Contains("2026_09_10_stage135_demo_operational_truth_reconciliation", runner, StringComparison.Ordinal);
        Assert.Contains("Stage135 demo POD placeholder cleanup is incomplete", runner, StringComparison.Ordinal);
        Assert.Contains("Stage135 contradictory demo job audit cleanup is incomplete", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO proof_of_delivery", batch2, StringComparison.Ordinal);
        Assert.DoesNotContain("'job.deleted','Job',5", batch7, StringComparison.Ordinal);
    }
}
