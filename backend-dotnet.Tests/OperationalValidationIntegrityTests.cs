using Opstrax.Api.Controllers;
using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

public sealed class OperationalValidationIntegrityTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(parts).ToArray()));

    [Theory]
    [InlineData("pending", "Pending")]
    [InlineData(" Active ", "Active")]
    [InlineData("SUSPENDED", "Suspended")]
    [InlineData("inactive", "Inactive")]
    public void CarrierStatuses_AreNormalizedToTheClosedVocabulary(string input, string expected)
    {
        Assert.True(CarrierStatusTransitionPolicy.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
        Assert.False(CarrierStatusTransitionPolicy.TryNormalize("Certified", out _));
    }

    [Fact]
    public void CarrierStatusTransitions_RequireReviewBeforeReactivation()
    {
        Assert.False(CarrierStatusTransitionPolicy.CanTransition("Inactive", "Active", out _, out _));
        Assert.True(CarrierStatusTransitionPolicy.CanTransition("Inactive", "Pending", out var pending, out _));
        Assert.Equal("Pending", pending);
        Assert.True(CarrierStatusTransitionPolicy.CanTransition("Pending", "Active", out var active, out _));
        Assert.Equal("Active", active);
    }

    [Fact]
    public void TelemetryAndSafetyRules_RejectUnimplementedRuleTypes()
    {
        Assert.True(TelemetryRuleTypePolicy.TryNormalizeTelemetry("Fuel_Drop_Pct", out var telemetry));
        Assert.Equal("fuel_drop_pct", telemetry);
        Assert.False(TelemetryRuleTypePolicy.TryNormalizeTelemetry("engine_temperature", out _));
        Assert.True(TelemetryRuleTypePolicy.TryNormalizeSafety("SAFETY_WEIGHT_SPEEDING", out var safety));
        Assert.Equal("safety_weight_speeding", safety);
        Assert.False(TelemetryRuleTypePolicy.TryNormalizeSafety("safety_arbitrary_rule", out _));
    }

    [Fact]
    public void ComplianceSubjects_AreRestrictedToOwnedFleetEntities()
    {
        Assert.True(ComplianceReferenceValidator.IsSupportedSubjectType("driver"));
        Assert.True(ComplianceReferenceValidator.IsSupportedSubjectType("vehicle"));
        Assert.False(ComplianceReferenceValidator.IsSupportedSubjectType("branch"));
        Assert.False(ComplianceReferenceValidator.IsSupportedSubjectType("transport"));

        var validator = Source("backend-dotnet", "Services", "OperationalValidationPolicies.cs");
        Assert.Contains("company_id=@companyId", validator);
        Assert.Contains("branch_id=@branchId", validator);
        Assert.Contains("deleted_at IS NULL", validator);

        var endpoints = Source("backend-dotnet", "Controllers", "MarketPackEndpoints.cs");
        Assert.True(Count(endpoints, "ComplianceReferenceValidator.ResolveAsync(") >= 3);
        Assert.Contains("subject_type, subject_id, subject_name", endpoints);
    }

    [Fact]
    public void LogisticsTextLimits_RejectOversizeFieldsWithoutSilentlyDroppingThem()
    {
        Assert.Null(FleetTmsLogisticsEndpoints.ValidateTextLength(new string('x', 120), "City", 120));
        Assert.Equal("City cannot exceed 120 characters.",
            FleetTmsLogisticsEndpoints.ValidateTextLength(new string('x', 121), "City", 120));
        Assert.Equal("Route notes cannot exceed 4000 characters.",
            FleetTmsLogisticsEndpoints.ValidateTextLength(new string('x', 4001), "Route notes", 4000));
    }

    [Fact]
    public void FleetHealthExcludesTerminalMaintenanceAndLegacySaudiRoutesAreGone()
    {
        var endpoints = Source("backend-dotnet", "Controllers", "EndpointMappings.cs");
        Assert.True(Count(endpoints, "LOWER(COALESCE(wo.status,'')) NOT IN ('completed','cancelled','closed','deleted')") >= 3);
        Assert.True(Count(endpoints, "LOWER(COALESCE(mi.status,'')) NOT IN ('completed','cancelled','closed','deleted')") >= 3);

        var coldChain = Source("backend-dotnet", "Controllers", "FleetTmsColdChainEndpoints.cs");
        Assert.DoesNotContain("/api/fleet-tms/compliance/documents", coldChain);
        Assert.DoesNotContain("/api/fleet-tms/compliance/expiries", coldChain);
        Assert.DoesNotContain("FleetReadinessDocumentRequest", coldChain);
    }

    private static int Count(string source, string value)
        => source.Split(value, StringSplitOptions.None).Length - 1;
}
