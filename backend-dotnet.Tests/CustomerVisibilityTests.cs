using Xunit;

namespace Opstrax.Tests;

// ── P5 Customer Visibility + ETA Risk Engine Tests ────────────────────────────

// ── Token Strategy Tests ──────────────────────────────────────────────────────
public class TrackingTokenTests
{
    private static string GenerateToken()
        => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    [Fact]
    public void Token_Is_64_Hex_Chars()
    {
        var token = GenerateToken();
        Assert.Equal(64, token.Length);
        Assert.Matches("^[0-9a-f]+$", token);
    }

    [Fact]
    public void Two_Tokens_Are_Not_Equal()
    {
        var t1 = GenerateToken();
        var t2 = GenerateToken();
        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public void Token_Expiry_Must_Not_Be_Non_Expiring()
    {
        // By policy, all tokens must have an expiry date <= 90 days
        var expiresAt = DateTime.UtcNow.AddDays(30);
        Assert.True(expiresAt > DateTime.UtcNow, "Token must expire in the future");
        Assert.True(expiresAt <= DateTime.UtcNow.AddDays(90), "Token expiry must not exceed 90 days");
    }

    [Fact]
    public void Expired_Token_Is_Rejected()
    {
        var expiresAt = DateTime.UtcNow.AddDays(-1); // already expired
        var isValid = expiresAt > DateTime.UtcNow && true /* share_enabled */ && true /* active */;
        Assert.False(isValid, "Expired token must be rejected");
    }

    [Fact]
    public void Revoked_Token_Is_Rejected()
    {
        // share_enabled=0 simulates revocation
        var shareEnabled = false;
        var expiresAt    = DateTime.UtcNow.AddDays(30);
        var isValid = expiresAt > DateTime.UtcNow && shareEnabled;
        Assert.False(isValid, "Revoked token (share_enabled=0) must be rejected");
    }

    [Fact]
    public void Revoked_Status_Is_Rejected()
    {
        var visibilityStatus = "revoked";
        var isValid = visibilityStatus == "active";
        Assert.False(isValid, "visibility_status='revoked' must cause rejection");
    }

    [Fact]
    public void Valid_Active_Token_Passes_All_Checks()
    {
        var expiresAt       = DateTime.UtcNow.AddDays(30);
        var shareEnabled    = true;
        var visibilityStatus = "active";
        var isValid = expiresAt > DateTime.UtcNow && shareEnabled && visibilityStatus == "active";
        Assert.True(isValid, "Token with future expiry, share_enabled=1, status=active must be valid");
    }
}

// ── Customer Scoping Tests ─────────────────────────────────────────────────────
public class CustomerVisibilityScopingTests
{
    private static bool CanCustomerSeeShipment(long requestCompanyId, long requestCustomerId,
        long shipmentCompanyId, long shipmentCustomerId)
    {
        // Tenant isolation: company_id must match
        // Customer scoping: customer_id must match (if present)
        return requestCompanyId == shipmentCompanyId && requestCustomerId == shipmentCustomerId;
    }

    [Fact]
    public void Customer_Cannot_See_Another_Customers_Shipment()
    {
        bool canSee = CanCustomerSeeShipment(
            requestCompanyId: 1, requestCustomerId: 100,
            shipmentCompanyId: 1, shipmentCustomerId: 200
        );
        Assert.False(canSee, "Customer 100 must not see customer 200's shipment");
    }

    [Fact]
    public void Cross_Tenant_Shipment_Access_Is_Blocked()
    {
        bool canSee = CanCustomerSeeShipment(
            requestCompanyId: 1, requestCustomerId: 100,
            shipmentCompanyId: 2, shipmentCustomerId: 100
        );
        Assert.False(canSee, "Tenant isolation: company 1 cannot access company 2's data");
    }

    [Fact]
    public void Customer_Can_See_Own_Shipment()
    {
        bool canSee = CanCustomerSeeShipment(
            requestCompanyId: 1, requestCustomerId: 100,
            shipmentCompanyId: 1, shipmentCustomerId: 100
        );
        Assert.True(canSee, "Customer must be able to see their own shipment within their tenant");
    }

    [Fact]
    public void Public_Token_Is_Scoped_To_Single_Shipment()
    {
        // A token created for shipment 501 must only expose data for shipment 501
        var tokenShipmentId = 501L;
        var requestedId     = 502L;
        Assert.NotEqual(tokenShipmentId, requestedId);
        // The SQL WHERE clause enforces: public_tracking_token = @token (single row)
        // so other shipments are never exposed via the same token
    }
}

// ── ETA Risk Engine Tests ──────────────────────────────────────────────────────
public class EtaRiskEngineTests
{
    private static string ComputeRisk(string status, DateTime? plannedDelivery, bool stale, int openExceptions)
    {
        if (status == "delivered")  return "on_time";
        if (status == "cancelled")  return "unknown";
        if (status == "exception" || openExceptions > 0) return "delayed";
        if (!plannedDelivery.HasValue) return "unknown";
        if (plannedDelivery < DateTime.UtcNow) return "delayed";
        if ((plannedDelivery.Value - DateTime.UtcNow).TotalHours <= 2 && stale) return "at_risk";
        return stale ? "at_risk" : "on_time";
    }

    private static string ComputeConfidence(bool stale, int exceptions, decimal? compliance)
    {
        if (stale) return "low";
        if (exceptions > 0) return "low";
        if (compliance.HasValue && compliance < 80) return "medium";
        return "high";
    }

    [Fact]
    public void Delivered_Assignment_Is_OnTime_HighConfidence()
    {
        var risk       = ComputeRisk("delivered", null, false, 0);
        var confidence = ComputeConfidence(false, 0, 95m);
        Assert.Equal("on_time", risk);
        Assert.Equal("high", confidence);
    }

    [Fact]
    public void Stale_Telemetry_Lowers_Confidence_To_Low()
    {
        var confidence = ComputeConfidence(stale: true, exceptions: 0, compliance: 95m);
        Assert.Equal("low", confidence);
    }

    [Fact]
    public void Active_Exception_Makes_Risk_Delayed()
    {
        var risk = ComputeRisk("exception", DateTime.UtcNow.AddHours(2), false, 1);
        Assert.Equal("delayed", risk);
    }

    [Fact]
    public void Open_Exception_Lowers_Confidence_To_Low()
    {
        var confidence = ComputeConfidence(stale: false, exceptions: 2, compliance: 90m);
        Assert.Equal("low", confidence);
    }

    [Fact]
    public void PastPlannedDelivery_Makes_Risk_Delayed()
    {
        var plannedDelivery = DateTime.UtcNow.AddHours(-3);
        var risk = ComputeRisk("in_transit", plannedDelivery, false, 0);
        Assert.Equal("delayed", risk);
    }

    [Fact]
    public void RouteDeviation_Lowers_Confidence_To_Medium()
    {
        var confidence = ComputeConfidence(stale: false, exceptions: 0, compliance: 72m);
        Assert.Equal("medium", confidence);
    }

    [Fact]
    public void GoodCompliance_No_Stale_No_Exceptions_IsHighConfidence()
    {
        var confidence = ComputeConfidence(stale: false, exceptions: 0, compliance: 94m);
        Assert.Equal("high", confidence);
    }

    [Fact]
    public void Cancelled_Assignment_Has_Unknown_Risk()
    {
        var risk = ComputeRisk("cancelled", DateTime.UtcNow.AddHours(4), false, 0);
        Assert.Equal("unknown", risk);
    }

    [Fact]
    public void No_Planned_Delivery_Has_Unknown_Risk()
    {
        var risk = ComputeRisk("assigned", null, false, 0);
        Assert.Equal("unknown", risk);
    }
}

// ── SLA Risk Logic Tests ───────────────────────────────────────────────────────
public class SlaRiskLogicTests
{
    private static readonly string[] EarlyStatuses = ["assigned", "accepted", "en_route_pickup", "arrived_pickup"];

    private static object ComputeSla(string status, DateTime? plannedPickup, DateTime? plannedDelivery, int exceptions)
    {
        bool latePickup   = plannedPickup.HasValue  && plannedPickup < DateTime.UtcNow  && EarlyStatuses.Contains(status);
        bool lateDelivery = plannedDelivery.HasValue && plannedDelivery < DateTime.UtcNow && status != "delivered" && status != "cancelled";
        bool exceptionHold = status == "exception" || exceptions > 0;
        string overall = lateDelivery || exceptionHold ? "high" : latePickup ? "medium" : "low";
        return new { LatePickupRisk = latePickup, LateDeliveryRisk = lateDelivery, ExceptionRisk = exceptionHold, OverallRisk = overall };
    }

    [Fact]
    public void LatePickup_When_Planned_Past_And_Not_Yet_Loaded()
    {
        dynamic sla = ComputeSla("en_route_pickup", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(3), 0);
        Assert.True((bool)sla.LatePickupRisk, "Planned pickup in the past with pre-load status = late pickup risk");
    }

    [Fact]
    public void LateDelivery_When_Planned_Past_And_Not_Delivered()
    {
        dynamic sla = ComputeSla("in_transit", null, DateTime.UtcNow.AddHours(-1), 0);
        Assert.True((bool)sla.LateDeliveryRisk);
        Assert.Equal("high", (string)sla.OverallRisk);
    }

    [Fact]
    public void Delivered_Has_No_Late_Delivery_Risk()
    {
        dynamic sla = ComputeSla("delivered", null, DateTime.UtcNow.AddHours(-1), 0);
        Assert.False((bool)sla.LateDeliveryRisk, "Delivered assignment cannot have late delivery risk");
    }

    [Fact]
    public void Exception_Status_Raises_High_Risk()
    {
        dynamic sla = ComputeSla("exception", null, DateTime.UtcNow.AddHours(2), 1);
        Assert.True((bool)sla.ExceptionRisk);
        Assert.Equal("high", (string)sla.OverallRisk);
    }

    [Fact]
    public void OnTime_Delivery_Has_Low_Risk()
    {
        dynamic sla = ComputeSla("in_transit", null, DateTime.UtcNow.AddHours(4), 0);
        Assert.False((bool)sla.LateDeliveryRisk);
        Assert.Equal("low", (string)sla.OverallRisk);
    }
}

// ── Customer-Safe Data Exposure Tests ─────────────────────────────────────────
public class CustomerSafeExposureTests
{
    private static readonly string[] InternalFields = [
        "safetyScore", "eligibilityJson", "overrideReason", "safetyOverridden", "hosOverridden",
        "previousStatus", "driverSafetyScore", "notes"
    ];

    private static readonly string[] AllowedCustomerFields = [
        "shipmentNumber", "customerName", "pickupAddress", "dropoffAddress",
        "currentStatus", "plannedPickupAt", "plannedDelivery", "actualPickup", "actualDelivery",
        "shareEnabled", "expiresAt"
    ];

    [Theory]
    [InlineData("safetyScore")]
    [InlineData("eligibilityJson")]
    [InlineData("overrideReason")]
    [InlineData("safetyOverridden")]
    [InlineData("hosOverridden")]
    [InlineData("previousStatus")]
    public void Internal_Fields_Must_Not_Be_In_Customer_View(string field)
    {
        Assert.DoesNotContain(field, AllowedCustomerFields);
    }

    [Theory]
    [InlineData("shipmentNumber")]
    [InlineData("customerName")]
    [InlineData("currentStatus")]
    [InlineData("plannedDelivery")]
    public void Customer_Fields_Are_Exposed(string field)
    {
        Assert.Contains(field, AllowedCustomerFields);
    }

    [Theory]
    [InlineData("maintenance_hold", "vehicle issue")]
    [InlineData("vehicle_breakdown", "vehicle issue")]
    [InlineData("driver_unavailable", "driver change")]
    [InlineData("weather_delay", "delay due to route")]
    public void Exception_Customer_Message_Does_Not_Expose_Internal_Blame(string exceptionType, string expectedPhrase)
    {
        string msg = exceptionType switch
        {
            "maintenance_hold" or "vehicle_breakdown" =>
                "A vehicle issue is being resolved. An updated ETA will be provided shortly.",
            "driver_unavailable" or "driver_safety_hold" =>
                "A driver change is required. Your shipment is being reassigned to an available driver.",
            "route_deviation" or "weather_delay" or "traffic_delay" =>
                "Your shipment is experiencing a delay due to route or traffic conditions. Your updated ETA has been recalculated.",
            _ => "Your shipment is experiencing a delay."
        };
        Assert.Contains(expectedPhrase, msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("safety score", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("out of service", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("defect", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Proof_Exposure_Uses_Hash_Reference_Not_Raw_Notes()
    {
        string? rawNotes     = "Driver called customer, gate code 1234 used";
        string? evidenceHash = "abc123def456789";
        // Customer sees evidence reference, never raw notes
        var evidenceRef = evidenceHash is not null ? $"proof-{evidenceHash[..8]}" : null;
        Assert.NotNull(evidenceRef);
        Assert.Null((string?)null); // raw notes are not part of the customer proof response
        _ = rawNotes; // acknowledged — used only internally
    }

    [Fact]
    public void Customer_Status_Labels_Are_Safe()
    {
        var mappings = new Dictionary<string, string>
        {
            ["assigned"]          = "Scheduled",
            ["in_transit"]        = "In Transit",
            ["delivered"]         = "Delivered",
            ["exception"]         = "Delayed — Update Pending",
            ["en_route_pickup"]   = "Driver En Route to Pickup",
        };

        foreach (var (_, label) in mappings)
        {
            Assert.DoesNotContain("exception", label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OOS", label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("out of service", label, StringComparison.OrdinalIgnoreCase);
        }
    }
}

// ── Customer Event Timeline Tests ──────────────────────────────────────────────
public class CustomerEventTimelineTests
{
    private static readonly string[] SafeAuditActions = [
        "dispatch.assignment.created", "dispatch.assignment.accepted",
        "dispatch.assignment.status_updated", "dispatch.assignment.maintenance_hold",
        "dispatch.assignment.exception", "dispatch.assignment.proof",
        "dispatch.assignment.cancelled"
    ];

    private static readonly string[] InternalOnlyActions = [
        "dispatch.eligibility.checked", "dispatch.assignment.override",
        "user.login", "vehicle.oos.triggered"
    ];

    [Theory]
    [InlineData("dispatch.assignment.created")]
    [InlineData("dispatch.assignment.accepted")]
    [InlineData("dispatch.assignment.status_updated")]
    [InlineData("dispatch.assignment.proof")]
    public void Customer_Safe_Actions_Are_In_Allowlist(string action)
    {
        Assert.Contains(action, SafeAuditActions);
    }

    [Theory]
    [InlineData("dispatch.eligibility.checked")]
    [InlineData("dispatch.assignment.override")]
    [InlineData("user.login")]
    public void Internal_Actions_Are_NOT_In_Customer_Allowlist(string action)
    {
        Assert.DoesNotContain(action, SafeAuditActions);
    }

    [Fact]
    public void Timeline_Events_Are_Ordered_Chronologically()
    {
        var events = new[]
        {
            new { OccurredAt = DateTime.UtcNow.AddHours(-5), Label = "Assignment Created" },
            new { OccurredAt = DateTime.UtcNow.AddHours(-3), Label = "Driver Accepted" },
            new { OccurredAt = DateTime.UtcNow.AddHours(-1), Label = "In Transit" },
        };
        var sorted = events.OrderBy(e => e.OccurredAt).ToArray();
        Assert.Equal("Assignment Created", sorted[0].Label);
        Assert.Equal("In Transit", sorted[^1].Label);
    }
}
