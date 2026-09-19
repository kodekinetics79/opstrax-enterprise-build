using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

internal static class CarrierStatusTransitionPolicy
{
    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Pending"] = new(StringComparer.OrdinalIgnoreCase) { "Pending", "Active", "Inactive" },
            ["Active"] = new(StringComparer.OrdinalIgnoreCase) { "Active", "Suspended", "Inactive" },
            ["Suspended"] = new(StringComparer.OrdinalIgnoreCase) { "Suspended", "Active", "Inactive" },
            // An inactive carrier must be returned to review before it can be activated again.
            ["Inactive"] = new(StringComparer.OrdinalIgnoreCase) { "Inactive", "Pending" },
        };

    internal static bool TryNormalize(string? value, out string normalized)
    {
        normalized = AllowedTransitions.Keys.FirstOrDefault(
            candidate => string.Equals(candidate, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "";
        return normalized.Length > 0;
    }

    internal static bool CanTransition(string? current, string? requested, out string normalized, out string error)
    {
        error = "Carrier status is not valid";
        if (!TryNormalize(requested, out normalized)) return false;
        if (!TryNormalize(current, out var currentStatus))
        {
            error = "Carrier has an unsupported current status and must be reviewed before it can be changed";
            return false;
        }

        if (AllowedTransitions[currentStatus].Contains(normalized)) return true;
        error = $"Carrier status cannot transition from {currentStatus} to {normalized}";
        return false;
    }
}

internal static class TelemetryRuleTypePolicy
{
    private static readonly HashSet<string> SupportedTelemetryRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "speeding", "stale_device", "idling", "fuel_drop_pct",
    };

    private static readonly HashSet<string> SupportedSafetyRules = new(StringComparer.OrdinalIgnoreCase)
    {
        "safety_weight_speeding",
        "safety_weight_repeated_speeding",
        "safety_weight_geofence_breach",
        "safety_weight_stale_device",
        "safety_coaching_required_score",
        "safety_repeated_speeding_threshold",
    };

    internal static bool TryNormalizeTelemetry(string? value, out string normalized)
        => TryNormalize(value, SupportedTelemetryRules, out normalized);

    internal static bool TryNormalizeSafety(string? value, out string normalized)
        => TryNormalize(value, SupportedSafetyRules, out normalized);

    private static bool TryNormalize(string? value, HashSet<string> allowed, out string normalized)
    {
        normalized = value?.Trim().ToLowerInvariant() ?? "";
        return allowed.Contains(normalized);
    }
}

internal sealed record ComplianceSubjectReference(long Id, string Type, string Name);

internal static class ComplianceReferenceValidator
{
    private static readonly HashSet<string> SupportedSubjectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "driver", "vehicle",
    };

    internal static bool IsSupportedSubjectType(string? subjectType)
        => SupportedSubjectTypes.Contains(subjectType?.Trim() ?? "");

    internal static async Task<(ComplianceSubjectReference? Reference, string? Error)> ResolveAsync(
        Database db,
        long companyId,
        long? branchId,
        string? subjectType,
        string? subjectId,
        string? subjectName,
        CancellationToken ct)
    {
        var type = subjectType?.Trim().ToLowerInvariant() ?? "";
        if (!SupportedSubjectTypes.Contains(type))
            return (null, "Subject type must be driver or vehicle.");

        var suppliedName = subjectName?.Trim() ?? "";
        var suppliedId = subjectId?.Trim() ?? "";
        var hasId = long.TryParse(suppliedId, out var parsedId) && parsedId > 0;
        if (suppliedId.Length > 0 && !hasId)
            return (null, "Subject identifier must be a positive integer.");
        if (!hasId && suppliedName.Length == 0)
            return (null, "A subject identifier or exact subject name is required.");

        var table = type == "driver" ? "drivers" : "vehicles";
        var nameColumn = type == "driver" ? "full_name" : "vehicle_code";
        var alternateNameColumn = type == "driver" ? "driver_code" : "plate_number";
        var matches = await db.QueryAsync(
            $@"SELECT id, {nameColumn} subject_name
               FROM {table}
               WHERE company_id=@companyId AND deleted_at IS NULL
                 AND (@branchId::BIGINT IS NULL OR branch_id=@branchId)
                 AND ((@subjectId::BIGINT IS NOT NULL AND id=@subjectId)
                   OR (@subjectId::BIGINT IS NULL AND
                       (LOWER(BTRIM({nameColumn}))=LOWER(BTRIM(@subjectName))
                        OR LOWER(BTRIM(COALESCE({alternateNameColumn},'')))=LOWER(BTRIM(@subjectName)))))
               ORDER BY id
               LIMIT 2",
            command =>
            {
                command.Parameters.AddWithValue("@companyId", companyId);
                command.Parameters.AddWithValue("@branchId", (object?)branchId ?? DBNull.Value);
                command.Parameters.AddWithValue("@subjectId", hasId ? (object)parsedId : DBNull.Value);
                command.Parameters.AddWithValue("@subjectName", suppliedName);
            }, ct);

        if (matches.Count != 1)
            return (null, $"{(type == "driver" ? "Driver" : "Vehicle")} must match exactly one active record in this tenant and branch.");

        var match = matches[0];
        return (new ComplianceSubjectReference(
            Convert.ToInt64(match["id"]),
            type,
            match.GetValueOrDefault("subjectName")?.ToString() ?? suppliedName), null);
    }
}
