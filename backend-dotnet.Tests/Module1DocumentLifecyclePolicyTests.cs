using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Independently authored from the frozen TRUTH012 decision/oracle. Pure policy and
// boundary checks only: not registered HTTP binding, RLS, persistence or certification.
public sealed class Module1DocumentLifecyclePolicyTests
{
    private static readonly DateOnly Today = new(2026, 8, 31);

    [Theory]
    [InlineData(-365, "Expired", 90, "Renewal Required", "Renew document")]
    [InlineData(-1, "Expired", 90, "Renewal Required", "Renew document")]
    [InlineData(0, "Expiring", 60, "Renewal Required", "Renew document")]
    [InlineData(1, "Expiring", 60, "Renewal Required", "Renew document")]
    [InlineData(30, "Expiring", 60, "Renewal Required", "Renew document")]
    [InlineData(31, "Active", 25, "Current", "Keep active in vault")]
    [InlineData(45, "Active", 25, "Current", "Keep active in vault")]
    public void Assessment_UsesExplicitUtcDayAndInclusiveThirtyDayWindow(int offset, string status,
        int risk, string renewal, string action)
    {
        var result = DocumentLifecyclePolicy.Assess(Today.AddDays(offset), Today);
        Assert.Equal(status, result.Status);
        Assert.Equal((decimal?)risk, result.RiskScore);
        Assert.Equal(renewal, result.RenewalStatus);
        Assert.Equal(action, result.RecommendedAction);
        Assert.Equal(Today, result.AssessmentDate);
        Assert.Equal("expiry-utc-30d-v1", result.PolicyVersion);
        Assert.Equal(DocumentLifecyclePolicy.PolicyVersion, result.PolicyVersion);
    }

    [Fact]
    public void Assessment_AbsentExpiryIsUnknown_NotZeroOrLow()
    {
        var result = DocumentLifecyclePolicy.Assess(null, Today);
        Assert.Equal("Unknown", result.Status);
        Assert.Null(result.RiskScore);
        Assert.Equal("Unknown", result.RenewalStatus);
        Assert.Equal("Add an expiry date or choose an explicit workflow override", result.RecommendedAction);
        Assert.Equal(Today, result.AssessmentDate);
    }

    [Theory]
    [InlineData(2024, 2, 29)]
    [InlineData(2026, 1, 31)]
    [InlineData(2026, 12, 31)]
    public void Assessment_CalendarBoundariesAndNextDayUseSuppliedDate(int year, int month, int day)
    {
        var t = new DateOnly(year, month, day);
        Assert.Equal("Expiring", DocumentLifecyclePolicy.Assess(t.AddDays(30), t).Status);
        Assert.Equal("Active", DocumentLifecyclePolicy.Assess(t.AddDays(31), t).Status);
        Assert.Equal("Expiring", DocumentLifecyclePolicy.Assess(t, t).Status);
        Assert.Equal("Expired", DocumentLifecyclePolicy.Assess(t, t.AddDays(1)).Status);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("legacy_unknown")]
    public void Preserve_DoesNotInferAutomaticOriginFromEqualDefaultTuple(string mode)
    {
        var existing = Snapshot(mode);
        var nextExpiry = Today.AddDays(45);
        var change = DocumentLifecyclePolicy.ApplyUpdate(existing, nextExpiry, Body("{}"), Today);
        Assert.Equal(existing with { ExpiresAt = nextExpiry }, change.Snapshot);
        Assert.Equal("preserve", change.Intent);
    }

    [Fact]
    public void Preserve_UnsupportedLegacyWorkflowStringsRemainPreservable()
    {
        var existing = Snapshot("legacy_unknown") with { Status = "Historical review", RenewalStatus = "Custom legacy", RecommendedAction = "Recorded legacy instruction" };
        var result = DocumentLifecyclePolicy.ApplyUpdate(existing, Today.AddDays(1), Body("{}"), Today);
        Assert.Equal(existing with { ExpiresAt = Today.AddDays(1) }, result.Snapshot);
    }

    [Fact]
    public void Automatic_ChangedExpiryReclassifies_ButSameDatePreservesStoredSnapshot()
    {
        var existing = Snapshot("automatic");
        var unchanged = DocumentLifecyclePolicy.ApplyUpdate(existing, existing.ExpiresAt, Body("{\"notes\":\"Synthetic metadata edit\"}"), Today);
        Assert.Equal(existing, unchanged.Snapshot);
        var changed = DocumentLifecyclePolicy.ApplyUpdate(existing, Today.AddDays(45), Body("{}"), Today).Snapshot;
        Assert.Equal("automatic", changed.Mode);
        Assert.Equal("Active", changed.Status);
        Assert.Equal(25m, changed.RiskScore);
        Assert.Equal("Current", changed.RenewalStatus);
        Assert.Equal("Keep active in vault", changed.RecommendedAction);
        Assert.Equal(Today, changed.AssessedOn);
        Assert.Equal(Today.AddDays(45), changed.ExpiresAt);
    }

    [Fact]
    public void ExplicitAutomatic_UsesReasonAndRecalculatesSameDateWithoutOriginInference()
    {
        var result = DocumentLifecyclePolicy.ApplyUpdate(Snapshot("legacy_unknown"), Today,
            Body("{\"lifecycleIntent\":\" automatic \",\"lifecycleReason\":\"  Explicit synthetic opt-in  \"}"), Today);
        Assert.Equal("automatic", result.Snapshot.Mode);
        Assert.Equal("Expiring", result.Snapshot.Status);
        Assert.Equal(60m, result.Snapshot.RiskScore);
        Assert.Equal(Today, result.Snapshot.AssessedOn);
        Assert.Equal("Explicit synthetic opt-in", result.Reason);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("100", "100")]
    [InlineData("1e2", "100")]
    [InlineData("0.25", "0.25")]
    [InlineData("\" 0.25 \"", "0.25")]
    [InlineData("\"0001.50\"", "1.50")]
    [InlineData("null", null)]
    public void Manual_RiskUsesInvariantNumberOrExplicitNull_AndClearsAssessment(string jsonRisk, string? expected)
    {
        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var result = DocumentLifecyclePolicy.ApplyUpdate(Snapshot("automatic"), Today, ManualBody(jsonRisk), Today).Snapshot;
            Assert.Equal("manual", result.Mode);
            Assert.Null(result.AssessedOn);
            Assert.Equal(expected is null ? null : decimal.Parse(expected, CultureInfo.InvariantCulture), result.RiskScore);
            Assert.Equal("Unknown", result.Status);
            Assert.Equal("Current", result.RenewalStatus);
            Assert.Equal("Synthetic workflow override", result.RecommendedAction);
        }
        finally { CultureInfo.CurrentCulture = priorCulture; }
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("\"1,5\"")]
    [InlineData("\"1e2\"")]
    [InlineData("\"+1\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("\".5\"")]
    [InlineData("\"1.\"")]
    [InlineData("101")]
    [InlineData("-0.01")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Manual_InvalidRiskRejected(string jsonRisk)
        => Error(400, () => DocumentLifecyclePolicy.ApplyUpdate(Snapshot("automatic"), Today, ManualBody(jsonRisk), Today));

    [Fact]
    public void Manual_TrimmedStringLimitsAreExact()
    {
        var body = ManualBody("0");
        body["lifecycleReason"] = new string('r', 500);
        body["recommendedAction"] = new string('a', 240);
        body["riskScore"] = new string('0', 32);
        var result = DocumentLifecyclePolicy.ApplyUpdate(Snapshot("automatic"), Today, body, Today);
        Assert.Equal(240, result.Snapshot.RecommendedAction!.Length);
        foreach (var field in new[] { "lifecycleReason", "recommendedAction", "riskScore" })
        {
            var invalid = new Dictionary<string, object?>(body);
            invalid[field] = new string(field == "riskScore" ? '0' : 'x', field == "lifecycleReason" ? 501 : field == "recommendedAction" ? 241 : 33);
            Error(400, () => DocumentLifecyclePolicy.ApplyUpdate(Snapshot("automatic"), Today, invalid, Today));
        }
    }

    [Theory]
    [InlineData("status")]
    [InlineData("renewalStatus")]
    [InlineData("riskScore")]
    [InlineData("recommendedAction")]
    [InlineData("lifecycleReason")]
    public void Manual_RequiresCompleteTupleAndReason(string field)
    {
        var body = ManualBody("null"); body.Remove(field);
        Error(400, () => DocumentLifecyclePolicy.ApplyUpdate(Snapshot("automatic"), Today, body, Today));
    }

    [Theory]
    [InlineData("status", "active")]
    [InlineData("status", "Available")]
    [InlineData("renewalStatus", "current")]
    [InlineData("renewalStatus", "Complete")]
    [InlineData("lifecycleIntent", "Automatic")]
    [InlineData("lifecycleReason", " ")]
    [InlineData("recommendedAction", " ")]
    public void Transition_NoncanonicalValuesRejected(string field, string value)
    {
        var body = ManualBody("0"); body[field] = value;
        Error(400, () => DocumentLifecyclePolicy.ApplyUpdate(Snapshot("automatic"), Today, body, Today));
    }

    [Theory]
    [InlineData("preserve")]
    [InlineData("automatic")]
    public void NonManualIntent_RejectsTupleFieldsEvenWhenValuesMatch(string intent)
    {
        foreach (var field in new[] { "status", "renewalStatus", "riskScore", "recommendedAction" })
        {
            var body = Body(JsonSerializer.Serialize(new { lifecycleIntent = intent, lifecycleReason = "Explicit synthetic opt-in" }));
            if (intent == "preserve") body.Remove("lifecycleReason");
            body[field] = field == "riskScore" ? 90 : field == "status" ? "Expired" : field == "renewalStatus" ? "Renewal Required" : "Renew document";
            Error(400, () => DocumentLifecyclePolicy.ApplyUpdate(Snapshot("automatic"), Today, body, Today));
        }
    }

    [Fact]
    public void QueueRenewal_RetainsExistingTuplePolicyAndProtectsSubsequentMetadataEdits()
    {
        var existing = Snapshot("automatic") with { RiskScore = 0m };
        var queue = DocumentLifecyclePolicy.QueueRenewal(existing).Snapshot;
        Assert.Equal(existing with { Mode = "manual", Status = "Expiring", RenewalStatus = "Renewal Queued",
            RecommendedAction = "Renewal queued by OpsTrax advisor", AssessedOn = null }, queue);
        var preserved = DocumentLifecyclePolicy.ApplyUpdate(queue, Today.AddDays(45), Body("{}"), Today).Snapshot;
        Assert.Equal(queue with { ExpiresAt = Today.AddDays(45) }, preserved);
    }

    [Theory]
    [InlineData("automatic")]
    [InlineData("manual")]
    public void QueuedMarkerReplacement_RequiresExplicitBooleanTrue(string intent)
    {
        var queued = Snapshot("legacy_unknown") with { RenewalStatus = "Renewal Queued" };
        var body = intent == "manual" ? ManualBody("0") : Body("{\"lifecycleIntent\":\"automatic\",\"lifecycleReason\":\"Explicit synthetic opt-in\"}");
        Error(409, () => DocumentLifecyclePolicy.ApplyUpdate(queued, Today, body, Today));
        body["replaceQueuedRenewal"] = false;
        Error(409, () => DocumentLifecyclePolicy.ApplyUpdate(queued, Today, body, Today));
        body["replaceQueuedRenewal"] = "true";
        Error(400, () => DocumentLifecyclePolicy.ApplyUpdate(queued, Today, body, Today));
        body["replaceQueuedRenewal"] = true;
        Assert.True(DocumentLifecyclePolicy.ApplyUpdate(queued, Today, body, Today).ReplaceQueuedRenewal);
    }

    [Fact]
    public void ManualKeepingQueuedMarker_DoesNotNeedReplacementAcknowledgment()
    {
        var body = ManualBody("null"); body["renewalStatus"] = "Renewal Queued";
        Assert.Equal("Renewal Queued", DocumentLifecyclePolicy.ApplyUpdate(
            Snapshot("legacy_unknown") with { RenewalStatus = "Renewal Queued" }, Today, body, Today).Snapshot.RenewalStatus);
    }

    [Theory]
    [InlineData("\"1\"", "1")]
    [InlineData("\"4294967295\"", "4294967295")]
    public void ExpectedVersion_AcceptsCanonicalOpaqueUInt32Strings(string json, string expected)
        => Assert.Equal(expected, DocumentLifecyclePolicy.RequireExpectedVersion(Body("{\"expectedVersion\":" + json + "}")));

    [Theory]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"0\"")]
    [InlineData("\"01\"")]
    [InlineData("\" 1\"")]
    [InlineData("\"1 \"")]
    [InlineData("\"+1\"")]
    [InlineData("\"-1\"")]
    [InlineData("\"1.0\"")]
    [InlineData("\"1e2\"")]
    [InlineData("\"4294967296\"")]
    [InlineData("\"10000000000\"")]
    public void ExpectedVersion_InvalidValuesAre400_NotCoerced(string json)
        => Error(400, () => DocumentLifecyclePolicy.RequireExpectedVersion(Body("{\"expectedVersion\":" + json + "}")));

    [Fact]
    public void ExpectedVersion_MissingIs428() => Error(428, () => DocumentLifecyclePolicy.RequireExpectedVersion(Body("{}")));

    [Theory]
    [InlineData("expectedVersion")]
    [InlineData("lifecycleIntent")]
    [InlineData("lifecycleReason")]
    [InlineData("replaceQueuedRenewal")]
    [InlineData("status")]
    [InlineData("renewalStatus")]
    [InlineData("riskScore")]
    [InlineData("recommendedAction")]
    [InlineData("lifecycleMode")]
    [InlineData("lifecycleAssessedOn")]
    [InlineData("currentDateAssessment")]
    [InlineData("assessmentDate")]
    [InlineData("policyVersion")]
    [InlineData("rowVersion")]
    public void Boundary_ExactDuplicateSensitiveJsonKeysRejectedBeforeCollapse(string key)
    {
        using var raw = JsonDocument.Parse("{\"" + key + "\":\"a\",\"" + key + "\":\"a\"}");
        Assert.NotEmpty(DocumentLifecyclePolicy.ValidateBoundary(raw.RootElement, DocumentWriteKind.Update));
    }

    [Theory]
    [InlineData("expected_version")]
    [InlineData("ExpectedVersion")]
    [InlineData("risk_score")]
    [InlineData("Status")]
    [InlineData("lifecycle_intent")]
    [InlineData("current_date_assessment")]
    [InlineData("clearExpiresAt")]
    public void Boundary_AliasesAndUnsupportedClearRejected(string key)
    {
        using var raw = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?> { [key] = "1" }));
        Assert.NotEmpty(DocumentLifecyclePolicy.ValidateBoundary(raw.RootElement, DocumentWriteKind.Update));
    }

    [Fact]
    public void Boundary_UnrelatedUnknownMetadataKeepsExistingPassThroughBehavior()
    {
        using var raw = JsonDocument.Parse("{\"clearanceCode\":\"synthetic\",\"clearUnrelatedFutureMetadata\":true}");
        Assert.Empty(DocumentLifecyclePolicy.ValidateBoundary(raw.RootElement, DocumentWriteKind.Update));
    }

    [Fact]
    public void CreationAndMultipart_RejectOwnedFieldsEvenAutomaticIntent_AndRepeatedValues()
    {
        foreach (var key in new[] { "lifecycleIntent", "lifecycleReason", "expectedVersion", "replaceQueuedRenewal", "status", "renewalStatus", "riskScore", "recommendedAction", "lifecycleMode", "rowVersion" })
        {
            using var raw = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?> { [key] = "automatic" }));
            Assert.NotEmpty(DocumentLifecyclePolicy.ValidateBoundary(raw.RootElement, DocumentWriteKind.Create));
            var form = new FormCollection(new Dictionary<string, StringValues> { [key] = new StringValues(["automatic", "automatic"]) });
            Assert.NotEmpty(DocumentLifecyclePolicy.ValidateFormBoundary(form, DocumentWriteKind.Create));
        }
        using var metadata = JsonDocument.Parse("{\"title\":\"Synthetic policy fixture\",\"entityType\":\"vehicle\",\"entityId\":1145}");
        Assert.Empty(DocumentLifecyclePolicy.ValidateBoundary(metadata.RootElement, DocumentWriteKind.Create));
    }

    [Theory]
    [InlineData("expectedVersion")]
    [InlineData("lifecycleIntent")]
    [InlineData("lifecycleReason")]
    [InlineData("replaceQueuedRenewal")]
    [InlineData("status")]
    [InlineData("renewalStatus")]
    [InlineData("riskScore")]
    [InlineData("recommendedAction")]
    public void Multipart_RepetitionIsRejectedIndependentlyOfCreateOwnedFieldRule(string key)
    {
        var single = new FormCollection(new Dictionary<string, StringValues> { [key] = "value" });
        Assert.Empty(DocumentLifecyclePolicy.ValidateFormBoundary(single, DocumentWriteKind.Update));
        var repeated = new FormCollection(new Dictionary<string, StringValues> { [key] = new StringValues(new[] { "value", "value" }) });
        Assert.NotEmpty(DocumentLifecyclePolicy.ValidateFormBoundary(repeated, DocumentWriteKind.Update));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("\"text\"")]
    public void Boundary_RequiresObject(string json)
    {
        using var raw = JsonDocument.Parse(json);
        Assert.NotEmpty(DocumentLifecyclePolicy.ValidateBoundary(raw.RootElement, DocumentWriteKind.Update));
    }

    [Fact]
    public void RenewBoundary_AllowsOnlyExpectedVersionAmongSensitiveCommands()
    {
        using var version = JsonDocument.Parse("{\"expectedVersion\":\"1\"}");
        Assert.Empty(DocumentLifecyclePolicy.ValidateBoundary(version.RootElement, DocumentWriteKind.Renew));
        foreach (var key in new[] { "lifecycleIntent", "lifecycleReason", "replaceQueuedRenewal", "status", "renewalStatus", "riskScore", "recommendedAction" })
        {
            using var raw = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object?> { [key] = "value" }));
            Assert.NotEmpty(DocumentLifecyclePolicy.ValidateBoundary(raw.RootElement, DocumentWriteKind.Renew));
        }
    }

    [Fact]
    public void Assessment_DateOnlyExtremesRemainBounded()
    {
        Assert.Equal("Expiring", DocumentLifecyclePolicy.Assess(DateOnly.MaxValue, DateOnly.MaxValue).Status);
        Assert.Equal("Expired", DocumentLifecyclePolicy.Assess(DateOnly.MinValue, DateOnly.MaxValue).Status);
        Assert.Equal("Active", DocumentLifecyclePolicy.Assess(DateOnly.MaxValue, DateOnly.MinValue).Status);
    }

    private static DocumentLifecycleSnapshot Snapshot(string mode) => new(mode, "Expired", 90m,
        "Renewal Required", "Renew document", mode == "automatic" ? Today.AddDays(-2) : null, Today.AddDays(-1));
    private static Dictionary<string, object?> Body(string json) => JsonSerializer.Deserialize<Dictionary<string, object?>>(json)!;
    private static Dictionary<string, object?> ManualBody(string risk) => Body("{\"lifecycleIntent\":\"manual\",\"lifecycleReason\":\"Synthetic explicit override\",\"status\":\" Unknown \",\"renewalStatus\":\" Current \",\"riskScore\":" + risk + ",\"recommendedAction\":\" Synthetic workflow override \"}");
    private static void Error(int status, Action action) => Assert.Equal(status, Assert.Throws<DocumentLifecycleException>(action).StatusCode);
}
