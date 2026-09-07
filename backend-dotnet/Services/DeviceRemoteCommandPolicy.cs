using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Opstrax.Api.Services;

public sealed record DeviceRemoteCommandRequest(
    string? CommandType,
    JsonElement Payload,
    string? Purpose,
    string? SourceReference,
    string? SafetyConfirmation,
    string? IdempotencyKey);

public sealed record DeviceRemoteCommandDefinition(
    string CommandType,
    string DisplayName,
    string CommandClass,
    int MaxAttempts,
    int TimeToLiveMinutes,
    string ConfirmationVerb);

public sealed record ValidatedDeviceRemoteCommand(
    DeviceRemoteCommandDefinition Definition,
    string PayloadJson,
    string Purpose,
    string SourceReference,
    string SafetyConfirmationHash,
    Guid IdempotencyKey);

/// <summary>
/// Defines the only remote-command shapes the control plane can admit. Provider
/// capability evidence is checked separately inside the database transaction.
/// Recording a request does not prove dispatch, acknowledgement, application,
/// device response, or physical outcome.
/// </summary>
public static class DeviceRemoteCommandPolicy
{
    private static readonly IReadOnlyDictionary<string, DeviceRemoteCommandDefinition> Definitions =
        new Dictionary<string, DeviceRemoteCommandDefinition>(StringComparer.Ordinal)
        {
            ["RequestPosition"] = new("RequestPosition", "Request position", "Observation", 1, 5, "REQUEST POSITION FROM"),
            ["RequestDiagnostics"] = new("RequestDiagnostics", "Request diagnostics", "Observation", 1, 10, "REQUEST DIAGNOSTICS FROM"),
            ["RestartDevice"] = new("RestartDevice", "Restart device", "Controlled", 1, 5, "RESTART"),
        };

    private static readonly HashSet<string> DiagnosticChannels = new(StringComparer.Ordinal)
    {
        "heartbeat", "power", "backup_battery", "cellular", "gnss", "ecm",
        "storage", "clock", "firmware", "configuration", "sim", "cable",
    };

    public static IReadOnlyCollection<DeviceRemoteCommandDefinition> Catalog => Definitions.Values.ToArray();

    public static (ValidatedDeviceRemoteCommand? Value, string? Error) Validate(
        DeviceRemoteCommandRequest? request, string deviceSerial)
    {
        if (request is null) return (null, "A remote-command request is required.");
        var commandType = Clean(request.CommandType);
        if (commandType is null || !Definitions.TryGetValue(commandType, out var definition))
            return (null, "commandType must be RequestPosition, RequestDiagnostics, or RestartDevice.");

        var purpose = Clean(request.Purpose);
        if (purpose is null || purpose.Length is < 10 or > 500)
            return (null, "purpose must contain 10-500 characters.");
        var source = Clean(request.SourceReference);
        if (source is null || source.Length is < 3 or > 240)
            return (null, "sourceReference must contain 3-240 characters.");
        if (!Guid.TryParseExact(Clean(request.IdempotencyKey), "D", out var idempotencyKey))
            return (null, "idempotencyKey must be a canonical UUID.");

        var expectedConfirmation = $"{definition.ConfirmationVerb} {deviceSerial}";
        if (!string.Equals(Clean(request.SafetyConfirmation), expectedConfirmation, StringComparison.Ordinal))
            return (null, $"safetyConfirmation must exactly match: {expectedConfirmation}");

        var payload = ValidatePayload(commandType, request.Payload);
        if (payload.Error is not null) return (null, payload.Error);
        return (new(definition, payload.Json!, purpose, source,
            HashConfirmation(expectedConfirmation), idempotencyKey), null);
    }

    public static string ConfirmationFor(string commandType, string deviceSerial) =>
        Definitions.TryGetValue(commandType, out var definition)
            ? $"{definition.ConfirmationVerb} {deviceSerial}"
            : string.Empty;

    private static (string? Json, string? Error) ValidatePayload(string commandType, JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            payload = JsonSerializer.SerializeToElement(new { });
        if (payload.ValueKind != JsonValueKind.Object)
            return (null, "payload must be a JSON object.");
        var properties = payload.EnumerateObject().ToArray();

        if (commandType == "RequestPosition")
            return properties.Length == 0
                ? ("{}", null)
                : (null, "RequestPosition payload must be empty.");

        if (commandType == "RequestDiagnostics")
        {
            if (properties.Any(property => property.Name != "channels"))
                return (null, "RequestDiagnostics payload only supports channels.");
            var channelProperties = properties.Where(property => property.Name == "channels").ToArray();
            if (channelProperties.Length == 0)
                return ("{}", null);
            if (channelProperties.Length != 1)
                return (null, "channels cannot be repeated.");
            var channelsProperty = channelProperties[0];
            if (channelsProperty.Value.ValueKind != JsonValueKind.Array)
                return (null, "channels must be an array.");
            var channels = new List<string>();
            foreach (var element in channelsProperty.Value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String ||
                    element.GetString() is not { } channel || !DiagnosticChannels.Contains(channel))
                    return (null, "channels contains an unsupported diagnostic channel.");
                channels.Add(channel);
            }
            if (channels.Count is < 1 or > 12 || channels.Distinct(StringComparer.Ordinal).Count() != channels.Count)
                return (null, "channels must contain 1-12 unique diagnostic channels.");
            return (JsonSerializer.Serialize(new { channels }), null);
        }

        if (properties.Any(property => property.Name != "delaySeconds"))
            return (null, "RestartDevice payload only supports delaySeconds.");
        var delayProperties = properties.Where(property => property.Name == "delaySeconds").ToArray();
        if (delayProperties.Length > 1)
            return (null, "delaySeconds cannot be repeated.");
        var delayProperty = delayProperties.SingleOrDefault();
        var delay = 0;
        if (delayProperty.Value.ValueKind != JsonValueKind.Undefined &&
            (!delayProperty.Value.TryGetInt32(out delay) || delay is < 0 or > 300))
            return (null, "delaySeconds must be a whole number from 0 to 300.");
        return (JsonSerializer.Serialize(new { delaySeconds = delay }), null);
    }

    private static string HashConfirmation(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
