using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class DeviceRmaPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidCasePreservesSeverityWarrantyAndExplicitTimes()
    {
        var result = DeviceRmaPolicy.ValidateCase(ValidCase(), Now);
        Assert.Null(result.Error);
        Assert.Equal("P1", result.Value!.Severity);
        Assert.Equal("ClaimedInWarranty", result.Value.WarrantyPosture);
        Assert.Equal(new DateTimeOffset(2026, 9, 7, 17, 30, 0, TimeSpan.Zero), result.Value.ObservedAt);
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("P4")]
    [InlineData("")]
    public void SeverityMustUseFactoryPolicy(string severity)
    {
        Assert.Contains("P0", DeviceRmaPolicy.ValidateCase(ValidCase() with { Severity = severity }, Now).Error);
    }

    [Fact]
    public void ClaimedWarrantyRequiresAReference()
    {
        var result = DeviceRmaPolicy.ValidateCase(ValidCase() with { WarrantyReference = null }, Now);
        Assert.Contains("warrantyReference", result.Error);
    }

    [Fact]
    public void DueTimeCannotPrecedeObservation()
    {
        var result = DeviceRmaPolicy.ValidateCase(ValidCase() with { ResponseDueAt = "2026-09-07T17:00:00Z" }, Now);
        Assert.Contains("at or after observedAt", result.Error);
    }

    [Fact]
    public void EventRequiresEvidenceAndPhysicalCustodyLocation()
    {
        var missingEvidence = DeviceRmaPolicy.ValidateEvent(ValidEvent() with { EvidenceReference = "" }, "Open", Now);
        Assert.Contains("evidenceReference", missingEvidence.Error);
        var missingLocation = DeviceRmaPolicy.ValidateEvent(ValidEvent() with { CustodyLocation = null }, "Open", Now);
        Assert.Contains("custodyLocation", missingLocation.Error);
    }

    [Fact]
    public void ResolvedCaseCannotAcceptMoreEvents()
    {
        var result = DeviceRmaPolicy.ValidateEvent(ValidEvent(), "Resolved", Now);
        Assert.Contains("resolved", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ReturnAuthorized", "AwaitingReturn")]
    [InlineData("Shipped", "InTransit")]
    [InlineData("Received", "UnderReview")]
    [InlineData("VendorDisposition", "UnderReview")]
    [InlineData("CaseClosed", "Resolved")]
    public void EventTypeDeterminesRecordedStatus(string eventType, string expected)
    {
        var result = DeviceRmaPolicy.ValidateEvent(ValidEvent() with
        {
            EventType = eventType,
            CustodyLocation = eventType is "Shipped" or "Received" ? "Carrier depot" : null,
        }, "Open", Now);
        Assert.Null(result.Error);
        Assert.Equal(expected, result.Value!.StatusAfter);
    }

    [Fact]
    public void ReplacementUsesExactRegisteredSerialAndAuditFacts()
    {
        var result = DeviceRmaPolicy.ValidateReplacement(new(
            " repl-00042 ", "Replace failed power unit", "Support ticket SUP-118", Guid.NewGuid().ToString("D")));
        Assert.Null(result.Error);
        Assert.Equal("REPL-00042", result.Value!.ReplacementDeviceSerial);
    }

    private static DeviceRmaCaseRequest ValidCase() => new(
        "P1", "Power", "Unit repeatedly loses vehicle power.", "2026-09-07T13:30:00-04:00",
        "ClaimedInWarranty", "Vendor policy W-42", "SLA-GOLD-4H", "2026-09-07T21:30:00Z",
        "Support ticket SUP-118", Guid.NewGuid().ToString("D"));

    private static DeviceRmaEventRequest ValidEvent() => new(
        "Shipped", "2026-09-07T17:45:00Z", "Carrier depot", "TRACK-118",
        "Shipment receipt SR-118", "Failed unit handed to carrier.", Guid.NewGuid().ToString("D"));
}
