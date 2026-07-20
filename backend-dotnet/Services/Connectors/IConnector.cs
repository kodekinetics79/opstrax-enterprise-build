using System.Text.Json;

namespace Opstrax.Api.Services.Connectors;

// ─────────────────────────────────────────────────────────────────────────────
// Connector framework — turns the Integrations marketplace from a status store
// into REAL, testable connectivity. Each provider implements IConnector; the
// registry resolves one by integration_key. TestConnectionAsync performs a genuine
// outbound handshake against the provider (real HTTP, real auth) and reports true
// success/failure — the marketplace only marks a connector "Connected" when the
// provider actually accepts the credentials.
//
// Credentials live in integrations.config_json. Sensitive keys (see
// ConnectorSecrets.SensitiveKeys) are encrypted at rest via PiiProtectionService and
// decrypted only here, in-process, at the moment of the outbound call.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record ConnectorResult(
    bool Success,
    string Message,
    IReadOnlyDictionary<string, object?>? Details = null)
{
    public static ConnectorResult Ok(string message, IReadOnlyDictionary<string, object?>? details = null)
        => new(true, message, details);
    public static ConnectorResult Fail(string message, IReadOnlyDictionary<string, object?>? details = null)
        => new(false, message, details);
}

public interface IConnector
{
    /// <summary>The integration_key(s) this connector handles (e.g. "twilio-sms").</summary>
    IReadOnlyCollection<string> Keys { get; }

    /// <summary>Human label for logs/UI.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Perform a REAL handshake with the provider using the decrypted config.
    /// Must make an actual outbound request and return the provider's verdict —
    /// never fabricate success.
    /// </summary>
    Task<ConnectorResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string?> config, CancellationToken ct);

    /// <summary>
    /// Optional live action (e.g. Twilio send-test-SMS). Default: not supported.
    /// action is a short verb the caller routes (e.g. "send-test").
    /// </summary>
    Task<ConnectorResult> RunActionAsync(
        string action, IReadOnlyDictionary<string, string?> config,
        JsonElement? body, CancellationToken ct)
        => Task.FromResult(ConnectorResult.Fail($"Action '{action}' is not supported by {DisplayName}."));
}
