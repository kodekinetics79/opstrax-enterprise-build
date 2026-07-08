using System.Net.Http.Headers;
using System.Text.Json;

namespace Opstrax.Api.Services.Connectors;

// Provider-specific connectors that authenticate against a real API-key endpoint.
// Each TestConnectionAsync makes a genuine outbound call and reports the provider's
// verdict — the marketplace marks them Connected only on a real success. Credentials
// are the sensitive config keys (apiKey/token), encrypted at rest.

// ── Slack ──────────────────────────────────────────────────────────────────────
// Config: token (Bot/User OAuth token, xoxb-…/xoxp-…) [SENSITIVE].
// Validates via auth.test — Slack returns { ok: true, team, url } for a live token.
public sealed class SlackConnector(IHttpClientFactory httpFactory, ILogger<SlackConnector> logger) : IConnector
{
    public IReadOnlyCollection<string> Keys { get; } = new[] { "slack" };
    public string DisplayName => "Slack";

    public async Task<ConnectorResult> TestConnectionAsync(IReadOnlyDictionary<string, string?> config, CancellationToken ct)
    {
        var token = config.GetValueOrDefault("token") ?? config.GetValueOrDefault("apiKey");
        if (string.IsNullOrWhiteSpace(token))
            return ConnectorResult.Fail("Add a Slack Bot/User OAuth token (xoxb-…) in Configure, then test again.");
        try
        {
            var client = httpFactory.CreateClient("connector-http");
            client.Timeout = TimeSpan.FromSeconds(12);
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/auth.test");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await client.SendAsync(req, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var ok = doc.RootElement.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
            if (ok)
            {
                var team = doc.RootElement.TryGetProperty("team", out var t) ? t.GetString() : null;
                return ConnectorResult.Ok($"Connected to Slack workspace{(team is null ? "" : $" \"{team}\"")}.",
                    new Dictionary<string, object?> { ["team"] = team });
            }
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "invalid_auth";
            return ConnectorResult.Fail($"Slack rejected the token ({err}).");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Slack test failed"); return ConnectorResult.Fail($"Could not reach Slack: {ex.Message}"); }
    }
}

// ── SendGrid ───────────────────────────────────────────────────────────────────
// Config: apiKey (SG.…) [SENSITIVE]. Validates via GET /v3/scopes.
public sealed class SendGridConnector(IHttpClientFactory httpFactory, ILogger<SendGridConnector> logger) : IConnector
{
    public IReadOnlyCollection<string> Keys { get; } = new[] { "sendgrid-email" };
    public string DisplayName => "SendGrid Email";

    public async Task<ConnectorResult> TestConnectionAsync(IReadOnlyDictionary<string, string?> config, CancellationToken ct)
    {
        var key = config.GetValueOrDefault("apiKey") ?? config.GetValueOrDefault("token");
        if (string.IsNullOrWhiteSpace(key))
            return ConnectorResult.Fail("Add a SendGrid API key (SG.…) in Configure, then test again.");
        try
        {
            var client = httpFactory.CreateClient("connector-http");
            client.Timeout = TimeSpan.FromSeconds(12);
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.sendgrid.com/v3/scopes");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var resp = await client.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return ConnectorResult.Ok("Connected to SendGrid — API key is valid.");
            if ((int)resp.StatusCode == 401) return ConnectorResult.Fail("SendGrid rejected the API key (401).");
            return ConnectorResult.Fail($"SendGrid returned {(int)resp.StatusCode} {resp.ReasonPhrase}.");
        }
        catch (Exception ex) { logger.LogWarning(ex, "SendGrid test failed"); return ConnectorResult.Fail($"Could not reach SendGrid: {ex.Message}"); }
    }
}

// ── Google Maps Platform ─────────────────────────────────────────────────────────
// Config: apiKey [SENSITIVE]. Validates via a tiny Geocoding request — Google returns
// status OK / REQUEST_DENIED (bad key) / etc., so we can report the real verdict.
public sealed class GoogleMapsConnector(IHttpClientFactory httpFactory, ILogger<GoogleMapsConnector> logger) : IConnector
{
    public IReadOnlyCollection<string> Keys { get; } = new[] { "google-maps-platform" };
    public string DisplayName => "Google Maps Platform";

    public async Task<ConnectorResult> TestConnectionAsync(IReadOnlyDictionary<string, string?> config, CancellationToken ct)
    {
        var key = config.GetValueOrDefault("apiKey");
        if (string.IsNullOrWhiteSpace(key))
            return ConnectorResult.Fail("Add a Google Maps API key in Configure, then test again.");
        try
        {
            var client = httpFactory.CreateClient("connector-http");
            client.Timeout = TimeSpan.FromSeconds(12);
            var url = $"https://maps.googleapis.com/maps/api/geocode/json?address=NewYork&key={Uri.EscapeDataString(key!)}";
            using var resp = await client.GetAsync(url, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "UNKNOWN";
            if (status is "OK" or "ZERO_RESULTS") return ConnectorResult.Ok("Connected to Google Maps Platform — API key is valid.");
            var msg = doc.RootElement.TryGetProperty("error_message", out var em) ? em.GetString() : status;
            return ConnectorResult.Fail($"Google Maps rejected the key ({status}): {msg}");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Google Maps test failed"); return ConnectorResult.Fail($"Could not reach Google Maps: {ex.Message}"); }
    }
}
