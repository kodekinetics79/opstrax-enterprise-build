using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Opstrax.Api.Services;

public enum DocumentWriteKind { Create, Update, Renew }

public sealed record DocumentDateAssessment(string Status, decimal? RiskScore, string RenewalStatus,
    string RecommendedAction, DateOnly AssessmentDate, string PolicyVersion);

public sealed record DocumentLifecycleSnapshot(string Mode, string Status, decimal? RiskScore,
    string RenewalStatus, string? RecommendedAction, DateOnly? AssessedOn, DateOnly? ExpiresAt);

public sealed record DocumentLifecycleChange(DocumentLifecycleSnapshot Snapshot, string Intent,
    string? Reason, bool ReplaceQueuedRenewal);

public sealed class DocumentLifecycleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Product expiry indicators, not a determination of legal validity.</summary>
public static class DocumentLifecyclePolicy
{
    public const string PolicyVersion = "expiry-utc-30d-v1";
    private static readonly string[] TupleFields = ["status", "riskScore", "renewalStatus", "recommendedAction"];
    private static readonly HashSet<string> ServerFields = new(StringComparer.Ordinal)
    {
        "lifecycleMode", "lifecycleAssessedOn", "currentDateAssessment", "assessmentDate", "policyVersion", "rowVersion"
    };
    private static readonly Dictionary<string, string> SensitiveNames = new[]
    {
        "lifecycleIntent", "lifecycleReason", "expectedVersion", "replaceQueuedRenewal", "status",
        "renewalStatus", "riskScore", "recommendedAction", "lifecycleMode", "lifecycleAssessedOn",
        "currentDateAssessment", "assessmentDate", "policyVersion", "rowVersion"
    }.ToDictionary(NormalizeName, value => value, StringComparer.Ordinal);

    public static DocumentDateAssessment Assess(DateOnly? expiry, DateOnly today)
    {
        if (expiry is null)
            return new("Unknown", null, "Unknown", "Add an expiry date or choose an explicit workflow override", today, PolicyVersion);
        if (expiry.Value < today)
            return new("Expired", 90m, "Renewal Required", "Renew document", today, PolicyVersion);
        // Day-number subtraction also handles DateOnly.MaxValue without AddDays overflow.
        if (expiry.Value.DayNumber - today.DayNumber <= 30)
            return new("Expiring", 60m, "Renewal Required", "Renew document", today, PolicyVersion);
        return new("Active", 25m, "Current", "Keep active in vault", today, PolicyVersion);
    }

    public static IReadOnlyList<string> ValidateBoundary(JsonElement body, DocumentWriteKind kind)
    {
        if (body.ValueKind != JsonValueKind.Object) return ["Document request must be a JSON object."];
        return ValidateNames(body.EnumerateObject().Select(property => (property.Name, 1)), kind);
    }

    public static IReadOnlyList<string> ValidateFormBoundary(IFormCollection form, DocumentWriteKind kind)
        => ValidateNames(form.Select(field => (field.Key, field.Value.Count)), kind);

    private static IReadOnlyList<string> ValidateNames(IEnumerable<(string Name, int Count)> fields, DocumentWriteKind kind)
    {
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, count) in fields)
        {
            var normalized = NormalizeName(name);
            if (!SensitiveNames.TryGetValue(normalized, out var canonical))
            {
                if (normalized.StartsWith("lifecycle", StringComparison.Ordinal)
                    || normalized is "clearexpiresat" or "clearissuedat" or "clearexpirydate" or "clearissueddate"
                    || normalized.StartsWith("replacequeued", StringComparison.Ordinal))
                    errors.Add("Unsupported document lifecycle command.");
                continue;
            }
            if (name != canonical || count != 1 || !seen.Add(normalized))
                errors.Add("Document lifecycle fields must use unique canonical names.");
            if (ServerFields.Contains(canonical))
                errors.Add("Document lifecycle provenance and assessment are server-owned.");
            else if (kind == DocumentWriteKind.Create)
                errors.Add("Document creation accepts metadata only; lifecycle is automatic.");
            else if (kind == DocumentWriteKind.Renew && canonical != "expectedVersion")
                errors.Add("Renewal accepts only its expectedVersion lifecycle field.");
        }
        return errors;
    }

    public static string RequireExpectedVersion(IReadOnlyDictionary<string, object?> body)
    {
        if (!body.TryGetValue("expectedVersion", out var raw))
            throw new DocumentLifecycleException(428, "Reload the document and submit its expectedVersion.");
        var text = NativeString(raw);
        if (text is null || !Regex.IsMatch(text, "^[1-9][0-9]{0,9}$", RegexOptions.CultureInvariant)
            || !uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw Invalid("expectedVersion must be a canonical positive UInt32 decimal string.");
        return text;
    }

    public static DocumentLifecycleChange ApplyUpdate(DocumentLifecycleSnapshot existing, DateOnly? effectiveExpiry,
        IReadOnlyDictionary<string, object?> body, DateOnly today)
    {
        var intent = body.TryGetValue("lifecycleIntent", out var rawIntent)
            ? RequiredString(rawIntent, 1, 20, "lifecycleIntent") : "preserve";
        if (intent is not ("preserve" or "automatic" or "manual")) throw Invalid("Unsupported lifecycleIntent.");
        var replace = false;
        if (body.TryGetValue("replaceQueuedRenewal", out var rawReplace))
        {
            replace = rawReplace switch
            {
                bool value => value,
                JsonElement { ValueKind: JsonValueKind.True } => true,
                JsonElement { ValueKind: JsonValueKind.False } => false,
                _ => throw Invalid("replaceQueuedRenewal must be a JSON boolean.")
            };
        }

        if (intent != "manual" && TupleFields.Any(body.ContainsKey))
            throw Invalid("Lifecycle tuple fields require an explicit manual override.");
        string? reason = null;
        if (intent != "preserve")
            reason = RequiredString(body.GetValueOrDefault("lifecycleReason"), 1, 500, "lifecycleReason");
        else if (body.ContainsKey("lifecycleReason"))
            throw Invalid("lifecycleReason requires an explicit lifecycle transition.");

        DocumentLifecycleSnapshot result;
        if (intent == "manual")
        {
            if (TupleFields.Any(field => !body.ContainsKey(field))) throw Invalid("Manual override requires all four lifecycle tuple fields.");
            var status = RequiredString(body["status"], 1, 30, "status");
            var renewal = RequiredString(body["renewalStatus"], 1, 40, "renewalStatus");
            if (status is not ("Active" or "Expiring" or "Expired" or "Unknown")) throw Invalid("Unsupported document status.");
            if (renewal is not ("Current" or "Renewal Required" or "Renewal Queued" or "Unknown")) throw Invalid("Unsupported renewalStatus.");
            result = new("manual", status, ParseRisk(body["riskScore"]), renewal,
                RequiredString(body["recommendedAction"], 1, 240, "recommendedAction"), null, effectiveExpiry);
        }
        else if (intent == "automatic" || (existing.Mode == "automatic" && effectiveExpiry != existing.ExpiresAt))
        {
            var assessment = Assess(effectiveExpiry, today);
            result = new("automatic", assessment.Status, assessment.RiskScore, assessment.RenewalStatus,
                assessment.RecommendedAction, today, effectiveExpiry);
        }
        else result = existing with { ExpiresAt = effectiveExpiry };

        if (existing.RenewalStatus == "Renewal Queued" && result.RenewalStatus != "Renewal Queued" && !replace)
            throw new DocumentLifecycleException(409, "A renewal is queued. Explicitly acknowledge replacing that workflow before continuing.");
        return new(result, intent, reason, replace);
    }

    public static DocumentLifecycleChange QueueRenewal(DocumentLifecycleSnapshot existing)
        => new(existing with
        {
            Mode = "manual", Status = "Expiring", RenewalStatus = "Renewal Queued",
            RecommendedAction = "Renewal queued by OpsTrax advisor", AssessedOn = null
        }, "renew", null, false);

    private static decimal? ParseRisk(object? raw)
    {
        if (raw is null || raw is JsonElement { ValueKind: JsonValueKind.Null }) return null;
        decimal value;
        if (raw is JsonElement { ValueKind: JsonValueKind.Number } number)
        {
            if (!number.TryGetDecimal(out value)) throw Invalid("riskScore must be finite and between 0 and 100.");
        }
        else if (NativeString(raw) is { } text)
        {
            text = text.Trim();
            if (text.Length > 32 || !Regex.IsMatch(text, "^[0-9]+(?:\\.[0-9]+)?$", RegexOptions.CultureInvariant)
                || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                throw Invalid("riskScore must be a finite number or explicit null.");
        }
        else if (raw is decimal decimalValue) value = decimalValue;
        else if (raw is int intValue) value = intValue;
        else if (raw is long longValue) value = longValue;
        else if (raw is double doubleValue && double.IsFinite(doubleValue) && doubleValue >= 0 && doubleValue <= 100) value = (decimal)doubleValue;
        else throw Invalid("riskScore must be a finite number or explicit null.");
        if (value < 0 || value > 100) throw Invalid("riskScore must be between 0 and 100.");
        return value;
    }

    private static string RequiredString(object? raw, int minimum, int maximum, string field)
    {
        var value = NativeString(raw)?.Trim();
        if (value is null || value.Length < minimum || value.Length > maximum)
            throw Invalid($"{field} must contain {minimum}–{maximum} characters.");
        return value;
    }

    private static string? NativeString(object? raw) => raw switch
    {
        string value => value,
        JsonElement { ValueKind: JsonValueKind.String } value => value.GetString(),
        _ => null
    };

    private static string NormalizeName(string name) => name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
    private static DocumentLifecycleException Invalid(string message) => new(400, message);
}
