using System.Globalization;
using System.Text.RegularExpressions;

namespace Opstrax.Api.Services;

public sealed record DeviceFirmwareCampaignRequest(
    string? CampaignName,
    string? TargetFirmwareVersion,
    string? RollbackFirmwareVersion,
    string? RolloutStrategy,
    string? ScheduledFor,
    int? MaintenanceWindowMinutes,
    int? BatchSize,
    long[]? DeviceIds,
    string? ChangeReason,
    string? SourceReference,
    string? IdempotencyKey);

public sealed record ValidatedDeviceFirmwareCampaign(
    string CampaignName,
    string TargetFirmwareVersion,
    string? RollbackFirmwareVersion,
    string RolloutStrategy,
    DateTimeOffset ScheduledFor,
    int MaintenanceWindowMinutes,
    int BatchSize,
    IReadOnlyList<long> DeviceIds,
    string ChangeReason,
    string SourceReference,
    Guid IdempotencyKey);

public sealed record FirmwareTargetPlanningAssessment(string Status, string Reason);

/// <summary>
/// Governs firmware rollout planning only. A planned target never means a remote
/// command was dispatched, acknowledged, applied, or supported by a provider/device.
/// </summary>
public static partial class DeviceFirmwareCampaignPolicy
{
    public static (ValidatedDeviceFirmwareCampaign? Value, string? Error) Validate(
        DeviceFirmwareCampaignRequest? request,
        DateTimeOffset now)
    {
        if (request is null) return (null, "A firmware campaign is required.");

        var name = Clean(request.CampaignName);
        if (name is null || name.Length is < 3 or > 160)
            return (null, "campaignName must contain 3-160 characters.");

        var targetVersion = Version(request.TargetFirmwareVersion);
        if (targetVersion is null)
            return (null, "targetFirmwareVersion must contain 1-120 version characters.");

        var rollbackVersion = Version(request.RollbackFirmwareVersion);
        if (Clean(request.RollbackFirmwareVersion) is not null && rollbackVersion is null)
            return (null, "rollbackFirmwareVersion must contain 1-120 version characters.");
        if (rollbackVersion is not null &&
            string.Equals(rollbackVersion, targetVersion, StringComparison.OrdinalIgnoreCase))
            return (null, "rollbackFirmwareVersion must differ from the target version.");

        var rolloutStrategy = Clean(request.RolloutStrategy) switch
        {
            "Manual" => "Manual",
            "Canary" => "Canary",
            "Staged" => "Staged",
            _ => null,
        };
        if (rolloutStrategy is null)
            return (null, "rolloutStrategy must be Manual, Canary, or Staged.");

        var scheduledText = Clean(request.ScheduledFor);
        if (scheduledText is null || !OffsetPattern().IsMatch(scheduledText) ||
            !DateTimeOffset.TryParse(scheduledText, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var scheduledFor))
            return (null, "scheduledFor must be an ISO-8601 timestamp with an explicit UTC offset.");
        scheduledFor = scheduledFor.ToUniversalTime();
        var utcNow = now.ToUniversalTime();
        if (scheduledFor < utcNow.AddMinutes(-5) || scheduledFor > utcNow.AddDays(365))
            return (null, "scheduledFor must be between five minutes ago and 365 days from now.");

        var maintenanceWindowMinutes = request.MaintenanceWindowMinutes ?? 0;
        if (maintenanceWindowMinutes is < 15 or > 720)
            return (null, "maintenanceWindowMinutes must be between 15 and 720.");

        var deviceIds = request.DeviceIds;
        if (deviceIds is null || deviceIds.Length is < 1 or > 500 || deviceIds.Any(id => id <= 0))
            return (null, "deviceIds must contain 1-500 positive device identifiers.");
        if (deviceIds.Distinct().Count() != deviceIds.Length)
            return (null, "deviceIds cannot contain duplicates.");

        var batchSize = request.BatchSize ?? 0;
        if (batchSize is < 1 or > 100 || batchSize > deviceIds.Length)
            return (null, "batchSize must be between 1 and 100 and cannot exceed the target count.");

        var reason = Clean(request.ChangeReason);
        if (reason is null || reason.Length is < 5 or > 500)
            return (null, "changeReason must contain 5-500 characters.");

        var source = Clean(request.SourceReference);
        if (source is null || source.Length is < 3 or > 240)
            return (null, "sourceReference must contain 3-240 characters.");

        if (!Guid.TryParseExact(Clean(request.IdempotencyKey), "D", out var idempotencyKey))
            return (null, "idempotencyKey must be a canonical UUID.");

        return (new(
            name,
            targetVersion,
            rollbackVersion,
            rolloutStrategy,
            scheduledFor,
            maintenanceWindowMinutes,
            batchSize,
            deviceIds,
            reason,
            source,
            idempotencyKey), null);
    }

    public static FirmwareTargetPlanningAssessment AssessTarget(
        string? manufacturer,
        string? deviceModel,
        string? hardwareRevision,
        string? reportedFirmwareVersion,
        string targetFirmwareVersion)
    {
        var reported = Clean(reportedFirmwareVersion);
        if (reported is not null && string.Equals(reported, targetFirmwareVersion, StringComparison.OrdinalIgnoreCase))
            return new("AlreadyCurrent", "The device already reports the requested target version.");

        var missing = new List<string>();
        if (Clean(manufacturer) is null) missing.Add("manufacturer");
        if (Clean(deviceModel) is null) missing.Add("device model");
        if (Clean(hardwareRevision) is null) missing.Add("hardware revision");
        if (reported is null) missing.Add("reported firmware version");
        if (missing.Count > 0)
            return new("BlockedIdentity", $"Missing exact identity: {string.Join(", ", missing)}.");

        return new(
            "ReadyForExternalEvidence",
            "Exact inventory identity is present. Provider capability and physical upgrade evidence remain required.");
    }

    private static string? Version(string? value)
    {
        var cleaned = Clean(value);
        return cleaned is not null && FirmwareVersionPattern().IsMatch(cleaned) ? cleaned : null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+\\-]{0,119}$", RegexOptions.CultureInvariant)]
    private static partial Regex FirmwareVersionPattern();

    [GeneratedRegex("(?:Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OffsetPattern();
}
