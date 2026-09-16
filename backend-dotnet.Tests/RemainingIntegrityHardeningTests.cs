using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class PlatformImpersonationMutationMatrixTests
{
    [Fact]
    public void EveryReviewedReadFamilyDeniesEveryStandardMutationVerb()
    {
        Assert.Equal(28, PlatformImpersonationPolicy.MutationDenialMatrix.Count);
        Assert.All(PlatformImpersonationPolicy.MutationDenialMatrix, entry =>
            Assert.False(PlatformImpersonationPolicy.IsReadOnlyRequestAllowed(entry.Method, entry.Path)));
        Assert.Equal(PlatformImpersonationPolicy.MutationDenialMatrix.Count,
            PlatformImpersonationPolicy.MutationDenialMatrix.Distinct().Count());
    }

    [Fact]
    public void LogoutIsTheOnlyAllowedTenantPlaneMutationAndUnknownMethodsFailClosed()
    {
        Assert.True(PlatformImpersonationPolicy.IsReadOnlyRequestAllowed("POST", "/api/auth/logout"));
        Assert.False(PlatformImpersonationPolicy.IsReadOnlyRequestAllowed("PUT", "/api/auth/logout"));
        Assert.False(PlatformImpersonationPolicy.IsReadOnlyRequestAllowed("TRACE", "/api/safety"));
        Assert.False(PlatformImpersonationPolicy.IsReadOnlyRequestAllowed("CONNECT", "/api/hos/logs"));
    }
}

public sealed class EldRecoveryEvidencePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MutableRegistryHealthFieldsAreNotPartOfTheDecisionContract()
    {
        var decision = EldRecoveryEvidencePolicy.Evaluate(new(
            "Internal", true, false, false, null, null, null), Now);

        Assert.True(decision.ProviderSupported);
        Assert.False(decision.ConnectivityAuthoritative);
        Assert.False(decision.CanActivate);
        Assert.False(decision.PreviewOnly);
    }

    [Fact]
    public void RecentAcceptedNativeHmacTelemetryWithCredentialsCanActivate()
    {
        var decision = EldRecoveryEvidencePolicy.Evaluate(new(
            "Internal", true, false, false, "native-hmac",
            Now.AddMinutes(-2), Now.AddMinutes(-1)), Now);

        Assert.True(decision.ConnectivityAuthoritative);
        Assert.True(decision.CanActivate);
        Assert.Equal("native-device-hmac", decision.VerificationLane);
    }

    [Theory]
    [InlineData("samsara-api", false)]
    [InlineData("native-hmac", true)]
    public void SamsaraRequiresVerifiedAccountAndCorrectAcceptedChannel(
        string channel, bool accountVerified)
    {
        var decision = EldRecoveryEvidencePolicy.Evaluate(new(
            "Samsara", false, true, accountVerified, channel,
            Now.AddMinutes(-2), Now.AddMinutes(-1)), Now);

        Assert.False(decision.CanActivate);
        Assert.Equal(channel == "samsara-api" && accountVerified,
            decision.ConnectivityAuthoritative);
    }

    [Fact]
    public void ProviderEvidenceCannotManufactureMissingDirectDeviceCredentials()
    {
        var decision = EldRecoveryEvidencePolicy.Evaluate(new(
            "Samsara", false, true, true, "samsara-api",
            Now.AddMinutes(-2), Now.AddMinutes(-1)), Now);

        Assert.True(decision.ProviderSupported);
        Assert.True(decision.ConnectivityAuthoritative);
        Assert.False(decision.CanActivate);
        Assert.False(decision.PreviewOnly);
    }

    [Theory]
    [InlineData(-16, -1)]
    [InlineData(-1, -16)]
    [InlineData(6, 0)]
    public void StaleOrImpossibleTimestampsFailClosed(int observedMinutes, int receivedMinutes)
    {
        var decision = EldRecoveryEvidencePolicy.Evaluate(new(
            "Internal", true, false, false, "native-hmac",
            Now.AddMinutes(observedMinutes), Now.AddMinutes(receivedMinutes)), Now);

        Assert.False(decision.ConnectivityAuthoritative);
        Assert.False(decision.CanActivate);
    }

    [Fact]
    public void UnsupportedProviderIsPreviewOnly()
    {
        var decision = EldRecoveryEvidencePolicy.Evaluate(new(
            "Synthetic fixture — no provider account", false, false, false,
            "native-hmac", Now.AddMinutes(-1), Now), Now);

        Assert.False(decision.ProviderSupported);
        Assert.False(decision.ConnectivityAuthoritative);
        Assert.False(decision.CanActivate);
        Assert.True(decision.PreviewOnly);
    }
}

public sealed class DetentionBatchEvaluationTests
{
    [Fact]
    public void SettledPricingLoadsTenantAndCustomerRuleCardsInOneTenantBoundBatch()
    {
        var sql = DetentionService.SettledPricingBatchSql;

        Assert.Contains("LEFT JOIN LATERAL", sql, StringComparison.Ordinal);
        Assert.Contains("candidate.company_id=d.company_id", sql, StringComparison.Ordinal);
        Assert.Contains("candidate.scope_id=d.customer_id", sql, StringComparison.Ordinal);
        Assert.Contains("CASE WHEN candidate.scope_type='customer' THEN 0 ELSE 1 END", sql, StringComparison.Ordinal);
        Assert.Contains("rc.id rule_card_id", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@cust", sql, StringComparison.Ordinal);
    }
}
