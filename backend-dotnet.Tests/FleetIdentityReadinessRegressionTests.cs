using System.IO;

namespace Opstrax.Tests;

public sealed class FleetIdentityReadinessRegressionTests
{
    [Fact]
    public void ProductionReadinessVerifiesExactUniqueValidReadyIndexContractsAndLedger()
    {
        var source = Read("backend-dotnet", "Services", "FleetProductionReadinessService.cs");

        Assert.Contains("identity_indexes(name, table_name, key1, key2, predicate)", source);
        Assert.Contains("i.indisunique AND i.indisvalid AND i.indisready", source);
        Assert.Contains("i.indnkeyatts=2 AND i.indnatts=2", source);
        Assert.Contains("pg_get_indexdef(i.indexrelid,1,true)=expected.key1", source);
        Assert.Contains("pg_get_indexdef(i.indexrelid,2,true)=expected.key2", source);
        Assert.Contains("pg_get_expr(i.indpred,i.indrelid,true)", source);
        Assert.Contains("2026_07_30_stage52_fleet_identity_uniqueness", source);
    }

    [Fact]
    public void PredeployPostcheckRejectsMissingInvalidNonUniqueOrDriftedSameNameIndexes()
    {
        var script = Read("tools", "apply-neon-predeploy-migrations.sh");

        Assert.Contains("i.indisunique AND i.indisvalid AND i.indisready", script);
        Assert.Contains("i.indrelid=to_regclass('public.'||expected.table_name)", script);
        Assert.Contains("pg_get_indexdef(i.indexrelid,2,true)=expected.key2", script);
        Assert.Contains("pg_get_expr(i.indpred,i.indrelid,true)", script);
        Assert.Contains("indexes missing, invalid, or drifted", script);
        Assert.Contains("Stage-52 Fleet identity migration ledger", script);
    }

    [Fact]
    public void Stage52DefinesNormalizedCodeAndActiveVinPlaintextAndBlindIndexUniqueness()
    {
        var migration = Read("database", "migrations", "2026_07_30_stage52_fleet_identity_uniqueness.sql");

        Assert.Contains("uq_vehicles_identity_code_normalized", migration);
        Assert.Contains("uq_drivers_identity_code_normalized", migration);
        Assert.Contains("uq_vehicles_active_vin_normalized", migration);
        Assert.Contains("uq_drivers_active_license_plaintext_normalized", migration);
        Assert.Contains("uq_drivers_active_license_bidx", migration);
        Assert.Contains("WHERE deleted_at IS NULL", migration);
        Assert.Contains("require reconciliation", migration);
        Assert.Contains("license_number LIKE 'enc:%'", migration);
        Assert.Contains("run a key-aware blind-index backfill", migration);
    }

    [Fact]
    public void PiiEnabledValidationBridgesLegacyPlaintextAndBlindIndexRows()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.True(Count(endpoints, "license_number_bidx=@bidx OR") >= 4);
        Assert.True(Count(endpoints,
            "NULLIF(BTRIM(license_number_bidx),'') IS NULL AND LOWER(BTRIM(license_number))=LOWER(BTRIM(") >= 4);
    }

    [Fact]
    public void ReadinessAndPredeployRejectEncryptedRowsWithoutBlindIndexes()
    {
        var readiness = Read("backend-dotnet", "Services", "FleetProductionReadinessService.cs");
        var predeploy = Read("tools", "apply-neon-predeploy-migrations.sh");

        Assert.Contains("license_number LIKE 'enc:%'", readiness);
        Assert.Contains("license_number LIKE 'enc:%'", predeploy);
        Assert.Contains("key-aware blind-index backfill", predeploy);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
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
