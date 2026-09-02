using System.Text.Json;
using System.Text.Json.Nodes;
using Opstrax.Api.Seed;
using Opstrax.Api.Security;

namespace Opstrax.Api.Services.Connectors;

// Resolves the right IConnector for an integration_key and centralizes credential
// crypto. Provider-specific connectors (Twilio, …) are matched by key. User-created
// custom connectors fall back to GenericHttpConnector, while catalog-only providers
// fail closed until a provider-specific adapter exists; endpoint reachability is not
// provider integration evidence.
public sealed class ConnectorRegistry
{
    private readonly Dictionary<string, IConnector> _byKey;
    private readonly GenericHttpConnector _fallback;
    private readonly PiiProtectionService _pii;
    private readonly bool _requiresEncryption;

    public ConnectorRegistry(
        IEnumerable<IConnector> connectors,
        GenericHttpConnector fallback,
        PiiProtectionService pii,
        IHostEnvironment environment)
    {
        _fallback = fallback;
        _pii = pii;
        // Staging frequently carries provider tokens and production-shaped data. It is
        // therefore a protected environment, not a development plaintext exception.
        _requiresEncryption = environment.IsProduction() || environment.IsStaging();
        _byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (var c in connectors)
            foreach (var k in c.Keys)
                _byKey[k] = c;
    }

    public IConnector Resolve(string? integrationKey)
    {
        if (integrationKey is not null && _byKey.TryGetValue(integrationKey, out var connector))
            return connector;
        return IntegrationCatalog.IsBuiltInKey(integrationKey)
            ? new CatalogOnlyConnector(IntegrationCatalog.DisplayNameFor(integrationKey))
            : _fallback;
    }

    // Config keys whose VALUES are secrets: encrypted at rest, decrypted only for the
    // outbound call, and redacted when the config is returned to the client. apiToken is
    // Samsara's documented name and must not fall through merely because apiKey/token are
    // also supported aliases.
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.Ordinal)
    {
        "authtoken", "apitoken", "apikey", "token", "secret", "clientsecret", "password",
        "privatekey", "hmacsecret", "accesstoken", "refreshtoken", "webhooksecret",
        "apikeysecret", "authsecret", "bearertoken", "consumersecret",
        "secretkey", "signingkey", "signingsecret", "connectionstring", "passphrase",
        "authorization",
    };

    // Free-form containers preserve their JSON shape for connector compatibility, but every
    // scalar leaf is secret. This closes aliases such as {"tokens":["live-secret"]} without
    // turning unrelated metadata such as authHeader/authScheme into credentials.
    private static readonly HashSet<string> SensitiveContainerKeys = new(StringComparer.Ordinal)
    {
        "credential", "credentials", "tokens", "apikeys",
    };

    // Normalize common provider naming conventions so apiToken, api_token, api-token,
    // and API TOKEN all hit the same sensitive-key registry.
    public static bool IsSensitive(string key)
    {
        var normalized = NormalizeKey(key);
        return SensitiveKeys.Contains(normalized) || SensitiveContainerKeys.Contains(normalized);
    }

    private static bool IsSensitiveContainer(string key) =>
        SensitiveContainerKeys.Contains(NormalizeKey(key));

    private static string NormalizeKey(string key) => new(
        key.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // Parse config_json into the connector's string map. Nested objects are preserved as
    // JSON strings for the existing IConnector contract, but every sensitive leaf is
    // recursively decrypted first. Protected environments reject legacy plaintext rather
    // than silently transmitting a DB-exposed credential to a provider.
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
                var value = Transform(prop.Value, prop.Name, SecretTransform.Decrypt);
                result[prop.Name] = value switch
                {
                    null => null,
                    string s => s,
                    _ => JsonSerializer.Serialize(value),
                };
            }
        }
        catch (ConnectorSecretProtectionException) { throw; }
        catch (JsonException) { /* malformed config -> empty map */ }
        return result;
    }

    // Encrypt sensitive values before persistence. Traversal is recursive so secrets inside
    // provider-specific "credentials", arrays, or custom connector objects cannot bypass the
    // registry. In Staging/Production, failure to produce an AES-GCM envelope is fatal.
    public string EncryptConfigForStorage(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
            return "{}";
        return JsonSerializer.Serialize(Transform(config, null, SecretTransform.Encrypt));
    }

    // Merge an update over the stored encrypted object without losing omitted credentials or
    // replacing them with the response mask. New/changed sensitive leaves are encrypted before
    // they enter the merged document, including leaves nested in objects and arrays.
    public string MergeConfigForStorage(JsonElement config, object? storedConfigRaw)
    {
        if (config.ValueKind != JsonValueKind.Object)
            return NormalizeStoredObject(storedConfigRaw).ToJsonString();
        var merged = MergeNode(NormalizeStoredObject(storedConfigRaw), config, null);
        return merged is JsonObject obj ? obj.ToJsonString() : "{}";
    }

    private static JsonObject NormalizeStoredObject(object? raw)
    {
        var json = raw as string ?? raw?.ToString();
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try { return JsonNode.Parse(json) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    private JsonNode? MergeNode(
        JsonNode? existing,
        JsonElement incoming,
        string? propertyName,
        bool forceSensitiveLeaf = false)
    {
        if (forceSensitiveLeaf || propertyName is not null && IsSensitiveContainer(propertyName))
            return MergeSensitiveContainerNode(existing, incoming);

        if (propertyName is not null && IsSensitive(propertyName))
        {
            if (IsRedactionMask(incoming)) return ProtectExistingWholeSecret(existing);
            // A provider may model a credential as a structured JSON value. Treat the
            // entire value as the secret, matching EncryptConfigForStorage, rather than
            // descending and accidentally exposing leaves under non-sensitive names.
            return JsonSerializer.SerializeToNode(TransformSecretValue(incoming, SecretTransform.Encrypt));
        }

        if (incoming.ValueKind == JsonValueKind.Object)
        {
            var result = existing is JsonObject existingObject
                ? (JsonObject)existingObject.DeepClone()
                : new JsonObject();
            foreach (var property in incoming.EnumerateObject())
                result[property.Name] = MergeNode(result[property.Name], property.Value, property.Name);
            return result;
        }

        if (incoming.ValueKind == JsonValueKind.Array)
        {
            var existingArray = existing as JsonArray;
            var result = new JsonArray();
            var index = 0;
            foreach (var item in incoming.EnumerateArray())
            {
                result.Add(MergeNode(existingArray is not null && index < existingArray.Count
                    ? existingArray[index]
                    : null, item, null));
                index++;
            }
            return result;
        }

        return JsonSerializer.SerializeToNode(Transform(incoming, propertyName, SecretTransform.Encrypt));
    }

    private JsonNode? MergeSensitiveContainerNode(JsonNode? existing, JsonElement incoming)
    {
        if (IsRedactionMask(incoming)) return ProtectExistingSecretContainer(existing);

        if (incoming.ValueKind == JsonValueKind.Object)
        {
            var protectedExisting = ProtectExistingSecretContainer(existing);
            var result = protectedExisting is JsonObject existingObject
                ? existingObject
                : new JsonObject();
            foreach (var property in incoming.EnumerateObject())
                result[property.Name] = MergeNode(
                    result[property.Name], property.Value, property.Name, forceSensitiveLeaf: true);
            return result;
        }

        if (incoming.ValueKind == JsonValueKind.Array)
        {
            var protectedExisting = ProtectExistingSecretContainer(existing) as JsonArray;
            var result = new JsonArray();
            var index = 0;
            foreach (var item in incoming.EnumerateArray())
            {
                result.Add(MergeNode(
                    protectedExisting is not null && index < protectedExisting.Count
                        ? protectedExisting[index]
                        : null,
                    item,
                    null,
                    forceSensitiveLeaf: true));
                index++;
            }
            return result;
        }

        return JsonSerializer.SerializeToNode(TransformSecretValue(incoming, SecretTransform.Encrypt));
    }

    private JsonNode? ProtectExistingSecretContainer(JsonNode? existing)
    {
        if (existing is JsonObject existingObject)
        {
            var protectedObject = new JsonObject();
            foreach (var property in existingObject)
                protectedObject[property.Key] = ProtectExistingSecretContainer(property.Value);
            return protectedObject;
        }

        if (existing is JsonArray existingArray)
        {
            var protectedArray = new JsonArray();
            foreach (var item in existingArray)
                protectedArray.Add(ProtectExistingSecretContainer(item));
            return protectedArray;
        }

        return ProtectExistingWholeSecret(existing);
    }

    private JsonNode? ProtectExistingWholeSecret(JsonNode? existing)
    {
        if (existing is null) return null;
        if (existing is JsonValue value && value.TryGetValue<string>(out var stored)
            && stored.StartsWith("enc:", StringComparison.Ordinal))
            return existing.DeepClone();

        using var document = JsonDocument.Parse(existing.ToJsonString());
        return JsonSerializer.SerializeToNode(
            TransformSecretValue(document.RootElement, SecretTransform.Encrypt));
    }

    private static bool IsRedactionMask(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String) return false;
        var value = element.GetString()?.Trim();
        if (string.IsNullOrEmpty(value)) return false;
        return value.Equals("[redacted]", StringComparison.OrdinalIgnoreCase)
               || value.Equals("***REDACTED***", StringComparison.OrdinalIgnoreCase)
               || (value.Length >= 4 && value.All(character => character is '*' or '•'));
    }

    // Redact secret values for display at every nesting level. This method intentionally does
    // not need an encryption service: even malformed/legacy plaintext is masked by key name.
    public static Dictionary<string, object?> RedactConfig(object? configJsonRaw)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var json = configJsonRaw as string ?? configJsonRaw?.ToString();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in doc.RootElement.EnumerateObject())
                result[prop.Name] = Redact(prop.Value, prop.Name);
        }
        catch (JsonException) { /* malformed -> empty */ }
        return result;
    }

    private enum SecretTransform { Encrypt, Decrypt }

    private object? Transform(
        JsonElement element,
        string? propertyName,
        SecretTransform operation,
        bool forceSensitiveLeaf = false)
    {
        if (forceSensitiveLeaf || propertyName is not null && IsSensitiveContainer(propertyName))
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => Transform(p.Value, p.Name, operation, forceSensitiveLeaf: true),
                    StringComparer.OrdinalIgnoreCase),
                JsonValueKind.Array => element.EnumerateArray()
                    .Select(item => Transform(item, null, operation, forceSensitiveLeaf: true)).ToList(),
                _ => TransformSecretValue(element, operation),
            };
        }

        if (propertyName is not null && IsSensitive(propertyName))
            return TransformSecretValue(element, operation);

        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                p => p.Name,
                p => Transform(p.Value, p.Name, operation),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(item => Transform(item, null, operation)).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText(),
        };
    }

    private object? TransformSecretValue(JsonElement element, SecretTransform operation)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        var stored = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
        if (string.IsNullOrEmpty(stored)) return stored;

        if (operation == SecretTransform.Encrypt)
        {
            if (_requiresEncryption && !_pii.Enabled)
                throw new ConnectorSecretProtectionException("Connector secret encryption is unavailable in this environment.");
            var encrypted = _pii.Encrypt(stored);
            if (_requiresEncryption && encrypted?.StartsWith("enc:", StringComparison.Ordinal) != true)
                throw new ConnectorSecretProtectionException("Connector secret encryption did not produce a protected envelope.");
            return encrypted;
        }

        if (_requiresEncryption && stored.StartsWith("enc:", StringComparison.Ordinal) != true)
            throw new ConnectorSecretProtectionException("A connector contains legacy plaintext credentials and must be reconfigured.");
        var plaintext = _pii.Decrypt(stored);
        if (stored.StartsWith("enc:", StringComparison.Ordinal) && plaintext is null)
            throw new ConnectorSecretProtectionException("A connector credential cannot be decrypted with the configured key set.");
        return plaintext;
    }

    private static object? Redact(
        JsonElement element,
        string? propertyName,
        bool forceSensitiveLeaf = false)
    {
        if (forceSensitiveLeaf || propertyName is not null && IsSensitiveContainer(propertyName))
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                    p => p.Name,
                    p => Redact(p.Value, p.Name, forceSensitiveLeaf: true),
                    StringComparer.OrdinalIgnoreCase),
                JsonValueKind.Array => element.EnumerateArray()
                    .Select(item => Redact(item, null, forceSensitiveLeaf: true)).ToList(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String when string.IsNullOrEmpty(element.GetString()) => "",
                _ => "••••••••",
            };
        }

        if (propertyName is not null && IsSensitive(propertyName))
            return element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : element.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(element.GetString())
                    ? ""
                    : "••••••••";

        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                p => p.Name,
                p => Redact(p.Value, p.Name),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(item => Redact(item, null)).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText(),
        };
    }
}

// Stable, value-free failure surfaced to endpoint/error handling. Never include a key name,
// ciphertext, provider response, or plaintext in this exception.
public sealed class ConnectorSecretProtectionException(string message) : InvalidOperationException(message);
