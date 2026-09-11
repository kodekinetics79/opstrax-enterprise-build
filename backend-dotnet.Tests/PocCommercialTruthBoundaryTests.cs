namespace Opstrax.Tests;

public sealed class PocCommercialTruthBoundaryTests
{
    [Fact]
    public void RetiredPodEndpoint_CannotPersistOrClaimPlaceholderProof()
    {
        var method = Block(EndpointSource(), "private static Task<IResult> ProofPlaceholderUnavailable(", "private static async Task<IResult> GetJobProof(");
        Assert.Contains("Status410Gone", method, StringComparison.Ordinal);
        Assert.Contains("authentic proof of delivery", method, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO proof_of_delivery", method, StringComparison.Ordinal);
        Assert.DoesNotContain("proof.placeholder.created", method, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogOnlyReport_CannotCreateCompletedRun()
    {
        var method = Block(EndpointSource(), "private static async Task<IResult> ReportRun(", "private static async Task<IResult> CreateScheduledReport(");
        Assert.Contains("dataset is null", method, StringComparison.Ordinal);
        Assert.Contains("No report run was recorded", method, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO report_runs", method, StringComparison.Ordinal);
        Assert.DoesNotContain("report.run_completed", method, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidencePackages_AreBranchScopedAndLockOnlyVerifiedEvidence()
    {
        var source = EndpointSource();
        var readBlock = Block(source, "private static async Task<IResult> EvidenceSummary(", "private static async Task<IResult> AiInsights(");
        var insertBlock = Block(source, "private static async Task<long> InsertEvidencePackage(", "private static async Task<long> InsertInsuranceReport(");

        Assert.Contains("StrictBranchFilter(http, \"ep\")", readBlock, StringComparison.Ordinal);
        Assert.Contains("branch_id=@branchId", readBlock, StringComparison.Ordinal);
        Assert.Contains("verificationStatus", readBlock, StringComparison.Ordinal);
        Assert.Contains("retrievalStatus", readBlock, StringComparison.Ordinal);
        Assert.Contains("status is controlled", readBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("without override", readBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INSERT INTO evidence_packages (company_id,branch_id", insertBlock, StringComparison.Ordinal);
        Assert.Contains("'incident_evidence'", insertBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("/placeholder/", insertBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholders bundled", insertBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InitialClientAdministrator_UsesBoundedTenantAdminRole()
    {
        var platform = Read("backend-dotnet", "Controllers", "PlatformEndpoints.cs");
        var invite = Block(platform, "private static async Task<AdminInviteResult> CreateAdminInviteAsync(", "private static async Task<string?> BuildTenantActivationUrlAsync(");
        Assert.Contains("role_name='Tenant Admin'", invite, StringComparison.Ordinal);
        Assert.Contains("@name, @email, 'Tenant Admin', 'Pending'", invite, StringComparison.Ordinal);
        Assert.DoesNotContain("@name, @email, 'Company Admin', 'Pending'", invite, StringComparison.Ordinal);
        Assert.Contains("[\"Tenant Admin\", \"Company Admin\"", platform, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformPasswordReset_IsActivePolicyBoundAndRevokesEveryCredentialPath()
    {
        var platform = Read("backend-dotnet", "Controllers", "PlatformEndpoints.cs");
        var reset = Block(platform, "private static async Task<IResult> TenantUserResetPassword(", "private static readonly string[] TenantAdminRoles");
        Assert.Contains("SecuritySettingsService securitySettings", reset, StringComparison.Ordinal);
        Assert.Contains("status", reset, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PasswordPolicyService.ValidatePassword", reset, StringComparison.Ordinal);
        Assert.Contains("RunInTenantTransactionAsync", reset, StringComparison.Ordinal);
        Assert.Contains("status='Active'", reset, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM user_sessions", reset, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM password_reset_tokens", reset, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceMigration_PreservesButExcludesLegacyGeneratedPlaceholders()
    {
        var migration = Read("database", "migrations", "2026_09_11_stage138_evidence_package_truth_boundary.sql");
        Assert.Contains("ADD COLUMN IF NOT EXISTS branch_id", migration, StringComparison.Ordinal);
        Assert.Contains("excludedFromEvidence", migration, StringComparison.Ordinal);
        Assert.Contains("legacy_generated_placeholder", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM evidence_package", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INSERT INTO schema_migrations", migration, StringComparison.Ordinal);
    }

    private static string EndpointSource() => Read("backend-dotnet", "Controllers", "EndpointMappings.cs");

    private static string Block(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to locate source block {startMarker}");
        return source[start..end];
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
