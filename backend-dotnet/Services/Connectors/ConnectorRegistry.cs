using System.Text.Json;
using Opstrax.Api.Security;

namespace Opstrax.Api.Services.Connectors;

// Resolves the right IConnector for an integration_key and centralizes credential
// crypto. Provider-specific connectors (Twilio, …) are matched by key; anything else
// falls back to the GenericHttpConnector so every connector — including user-created
// custom ones — is testable against its real endpoint.
public sealed class ConnectorRegistry
{
    private readonly Dictionary<string, IConnector> _byKey;
    private readonly GenericHttpConnector _fallback;
    private readonly PiiProtectionService _pii;

    public ConnectorRegistry(IEnumerable<IConnector> connectors, GenericHttpConnector fallback, PiiProtectionService pii)
    {
        _fallback = fallback;
        _pii = pii;
        _byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connectors)
            foreach (var k in c.Keys)
                _byKey[k] = c;
    }

    public IConnector Resolve(string? integrationKey)
        => integrationKey is not null && _byKey.TryGetValue(integrationKey, out var c) ? c : _fallback;

    // Config keys whose VALUES are secrets: encrypted at rest, decrypted only for the
    // outbound call, and redacted when the config is returned to the client.
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "authToken", "apiKey", "token", "secret", "clientSecret", "password",
        "privateKey", "hmacSecret", "accessToken", "refreshToken", "webhookSecret",
        "apiKeySecret", "authSecret",
    };

    public static bool IsSensitive(string key) => SensitiveKeys.Contains(key);

    // Parse config_json (a JSON string from the DB) into a flat string map, decrypting
    // any enc:-wrapped secret values so the connector gets usable plaintext credentials.
    public IReadOnlyDictionary<string, string?> DecryptConfig(object? configJsonRaw)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var json = configJsonRaw as string ?? configJsonRaw?.ToString();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var raw = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                result[prop.Name] = IsSensitive(prop.Name) ? _pii.Decrypt(raw) : raw;
            }
        }
        catch { /* malformed config → empty map */ }
        return result;
    }

    // Encrypt sensitive values in an incoming config object before it is persisted, so
    // provider secrets are never stored in plaintext. Returns a JSON string for config_json.
    public string EncryptConfigForStorage(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
            return "{}";
        var obj = new Dictionary<string, object?>();
        foreach (var prop in config.EnumerateObject())
        {
            if (IsSensitive(prop.Name) && prop.Value.ValueKind == JsonValueKind.String)
            {
                var plain = prop.Value.GetString();
                obj[prop.Name] = string.IsNullOrEmpty(plain) ? plain : _pii.Encrypt(plain);
            }
            else
            {
                obj[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.GetRawText(),
                };
            }
        }
        return JsonSerializer.Serialize(obj);
    }

    // Redact secret values for display: never return decrypted secrets to the client.
    public static Dictionary<string, object?> RedactConfig(object? configJsonRaw)
    {
        var result = new Dictionary<string, object?>();
        var json = configJsonRaw as string ?? configJsonRaw?.ToString();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (IsSensitive(prop.Name))
                {
                    var v = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                    result[prop.Name] = string.IsNullOrEmpty(v) ? "" : "••••••••"; // present-but-hidden
                }
                else
                {
                    result[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText(),
                    };
                }
            }
        }
        catch { /* malformed → empty */ }
        return result;
    }
}
