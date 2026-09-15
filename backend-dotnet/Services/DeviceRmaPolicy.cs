using System.Globalization;

namespace Opstrax.Api.Services;

public sealed record DeviceRmaCaseRequest(
    string? Severity,
    string? FailureCategory,
    string? FailureDescription,
    string? ObservedAt,
    string? WarrantyPosture,
    string? WarrantyReference,
    string? SupportSlaReference,
    string? ResponseDueAt,
    string? SourceReference,
    string? IdempotencyKey);

public sealed record ValidatedDeviceRmaCase(
    string Severity,
    string FailureCategory,
    string FailureDescription,
    DateTimeOffset ObservedAt,
    string WarrantyPosture,
    string? WarrantyReference,
    string SupportSlaReference,
    DateTimeOffset ResponseDueAt,
    string SourceReference,
    Guid IdempotencyKey);

public sealed record DeviceRmaEventRequest(
    string? EventType,
    string? OccurredAt,
    string? CustodyLocation,
    string? TrackingReference,
    string? EvidenceReference,
    string? Notes,
    string? IdempotencyKey);

public sealed record ValidatedDeviceRmaEvent(
    string EventType,
    string StatusAfter,
    DateTimeOffset OccurredAt,
    string? CustodyLocation,
    string? TrackingReference,
    string EvidenceReference,
    string Notes,
    Guid IdempotencyKey);

public sealed record DeviceRmaReplacementRequest(
    string? ReplacementDeviceSerial,
    string? ChangeReason,
    string? SourceReference,
    string? IdempotencyKey);

public sealed record ValidatedDeviceRmaReplacement(
    string ReplacementDeviceSerial,
    string ChangeReason,
    string SourceReference,
    Guid IdempotencyKey);

/// <summary>
/// Validates software-recorded RMA facts. These records do not prove physical
/// custody, warranty acceptance, shipment delivery, installation, or readiness.
/// </summary>
public static class DeviceRmaPolicy
{
    private static readonly HashSet<string> Severities = ["P0", "P1", "P2", "P3"];
    private static readonly HashSet<string> FailureCategories =
    ["Power", "Connectivity", "GNSS", "CAN", "Camera", "Firmware", "PhysicalDamage", "Intermittent", "Other"];
    private static readonly HashSet<string> WarrantyPostures =
    ["Unknown", "ClaimedInWarranty", "ClaimedOutOfWarranty", "NotApplicable"];
    private static readonly Dictionary<string, string> EventStatuses = new(StringComparer.Ordinal)
    {
        ["ReturnAuthorized"] = "AwaitingReturn",
        ["Shipped"] = "InTransit",
        ["Received"] = "UnderReview",
        ["VendorDisposition"] = "UnderReview",
        ["CaseClosed"] = "Resolved",
    };

    public static (ValidatedDeviceRmaCase? Value, string? Error) ValidateCase(
        DeviceRmaCaseRequest? request, DateTimeOffset now)
    {
        if (request is null) return (null, "An RMA case is required.");
        var severity = Clean(request.Severity);
        if (severity is null || !Severities.Contains(severity))
            return (null, "severity must be P0, P1, P2, or P3.");
        var category = Clean(request.FailureCategory);
        if (category is null || !FailureCategories.Contains(category))
            return (null, "failureCategory is not supported.");
        var description = Clean(request.FailureDescription);
        if (description is null || description.Length is < 10 or > 1000)
            return (null, "failureDescription must contain 10-1000 characters.");
        var observed = Timestamp(request.ObservedAt);
        var utcNow = now.ToUniversalTime();
        if (observed is null || observed < utcNow.AddDays(-365) || observed > utcNow.AddMinutes(5))
            return (null, "observedAt must be an explicit-offset timestamp from the last 365 days and no more than five minutes in the future.");
        var warranty = Clean(request.WarrantyPosture);
        if (warranty is null || !WarrantyPostures.Contains(warranty))
            return (null, "warrantyPosture is not supported.");
        var warrantyReference = Clean(request.WarrantyReference);
        if (warranty is "ClaimedInWarranty" or "ClaimedOutOfWarranty" && warrantyReference is null)
            return (null, "warrantyReference is required for a claimed warranty posture.");
        if (warrantyReference?.Length > 240)
            return (null, "warrantyReference cannot exceed 240 characters.");
        var sla = Clean(request.SupportSlaReference);
        if (sla is null || sla.Length is < 3 or > 240)
            return (null, "supportSlaReference must contain 3-240 characters.");
        var due = Timestamp(request.ResponseDueAt);
        if (due is null || due < observed || due > utcNow.AddDays(365))
            return (null, "responseDueAt must be an explicit-offset timestamp at or after observedAt and within 365 days.");
        var source = Clean(request.SourceReference);
        if (source is null || source.Length is < 3 or > 240)
            return (null, "sourceReference must contain 3-240 characters.");
        if (!Guid.TryParseExact(Clean(request.IdempotencyKey), "D", out var idempotency))
            return (null, "idempotencyKey must be a canonical UUID.");
        return (new(severity, category, description, observed.Value, warranty,
            warrantyReference, sla, due.Value, source, idempotency), null);
    }

    public static (ValidatedDeviceRmaEvent? Value, string? Error) ValidateEvent(
        DeviceRmaEventRequest? request, string currentStatus, DateTimeOffset now)
    {
        if (request is null) return (null, "An RMA custody event is required.");
        if (currentStatus == "Resolved") return (null, "A resolved RMA case cannot accept more events.");
        var eventType = Clean(request.EventType);
        if (eventType is null || !EventStatuses.TryGetValue(eventType, out var statusAfter))
            return (null, "eventType must be ReturnAuthorized, Shipped, Received, VendorDisposition, or CaseClosed.");
        var occurred = Timestamp(request.OccurredAt);
        var utcNow = now.ToUniversalTime();
        if (occurred is null || occurred < utcNow.AddDays(-365) || occurred > utcNow.AddMinutes(5))
            return (null, "occurredAt must be an explicit-offset timestamp from the last 365 days and no more than five minutes in the future.");
        var evidence = Clean(request.EvidenceReference);
        if (evidence is null || evidence.Length is < 3 or > 240)
            return (null, "evidenceReference must contain 3-240 characters.");
        var notes = Clean(request.Notes);
        if (notes is null || notes.Length is < 5 or > 1000)
            return (null, "notes must contain 5-1000 characters.");
        var location = Clean(request.CustodyLocation);
        var tracking = Clean(request.TrackingReference);
        if (location?.Length > 240 || tracking?.Length > 240)
            return (null, "custodyLocation and trackingReference cannot exceed 240 characters.");
        if (eventType is "Shipped" or "Received" && location is null)
            return (null, "custodyLocation is required for shipped and received events.");
        if (!Guid.TryParseExact(Clean(request.IdempotencyKey), "D", out var idempotency))
            return (null, "idempotencyKey must be a canonical UUID.");
        return (new(eventType, statusAfter, occurred.Value, location, tracking,
            evidence, notes, idempotency), null);
    }

    public static (ValidatedDeviceRmaReplacement? Value, string? Error) ValidateReplacement(
        DeviceRmaReplacementRequest? request)
    {
        if (request is null) return (null, "A replacement plan is required.");
        var serial = Clean(request.ReplacementDeviceSerial)?.ToUpperInvariant();
        if (serial is null || serial.Length is < 3 or > 120)
            return (null, "replacementDeviceSerial must contain 3-120 characters.");
        var reason = Clean(request.ChangeReason);
        if (reason is null || reason.Length is < 5 or > 500)
            return (null, "changeReason must contain 5-500 characters.");
        var source = Clean(request.SourceReference);
        if (source is null || source.Length is < 3 or > 240)
            return (null, "sourceReference must contain 3-240 characters.");
        if (!Guid.TryParseExact(Clean(request.IdempotencyKey), "D", out var idempotency))
            return (null, "idempotencyKey must be a canonical UUID.");
        return (new(serial, reason, source, idempotency), null);
    }

    private static DateTimeOffset? Timestamp(string? value)
    {
        var cleaned = Clean(value);
        if (cleaned is null || !(cleaned.EndsWith('Z') ||
            (cleaned.Length >= 6 && (cleaned[^6] is '+' or '-') && cleaned[^3] == ':')))
            return null;
        return DateTimeOffset.TryParse(cleaned, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime() : null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
