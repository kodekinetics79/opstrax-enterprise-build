using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class RuntimeReadBlockerSourceRegressionTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Fact]
    public void FeatureFlagActorColumn_IsOwnedByRuntimeSchemaAndEnrolledMigration()
    {
        var schema = ReadSource("backend-dotnet", "Services", "FeatureFlagSchemaService.cs");
        var migration = ReadSource("database", "migrations", "2026_09_11_stage140_feature_flag_actor_contract.sql");
        var runner = ReadSource("tools", "apply-neon-predeploy-migrations.sh");

        Assert.Contains("updated_by   VARCHAR(220) NULL", schema, StringComparison.Ordinal);
        Assert.Contains("(\"updated_by\",  \"VARCHAR(220) NULL\")", schema, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS updated_by VARCHAR(220) NULL", migration, StringComparison.Ordinal);
        Assert.Contains("2026_09_11_stage140_feature_flag_actor_contract", runner, StringComparison.Ordinal);
        Assert.Contains("('feature_flags','updated_by')", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void Campaigns_UseTheTenantScopedCommercialRegisterAndCrmPermissions()
    {
        var endpoints = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");

        Assert.Contains("MapDedicatedModule(app, \"campaigns\")", endpoints, StringComparison.Ordinal);
        Assert.Contains("[\"campaigns\"] = \"customers:view\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("[\"campaigns\"] = \"customers:update\"", endpoints, StringComparison.Ordinal);
    }
}

[Trait("Category", "Integration")]
public sealed class RuntimeReadBlockerPostgresTests
{
    [Fact]
    public async Task NullableFinanceFilters_HaveExplicitDatabaseTypesAndExecute()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
                ["Rls:EnforceTenantContext"] = "false"
            })
            .Build();
        var db = new Database(configuration);

        // A company id outside the seeded range keeps this a read-only contract test.
        const long absentCompanyId = -9_999_999;
        var billingRuns = await new BillingConsolidationService(db)
            .ListRunsAsync(absentCompanyId, customerId: null, status: null);
        var settlements = await new SettlementService(db)
            .ListStatementsAsync(absentCompanyId, payeeType: null, payeeId: null, status: null);
        var recognitionEntries = await new RevenueRecognitionService(db)
            .ListEntriesAsync(absentCompanyId, periodCode: null, status: null, customerId: null);

        Assert.Empty(billingRuns);
        Assert.Empty(settlements);
        Assert.Empty(recognitionEntries);
    }
}
