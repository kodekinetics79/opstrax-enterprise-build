using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Opstrax.Api.Services.Connectors;

// Real Twilio connector. TestConnection validates the Account SID + Auth Token by
// calling Twilio's REST API (GET /Accounts/{sid}.json) — a 200 means the credentials
// are live and correct, a 401 means they are wrong. This is genuine connectivity, not
// a status flip. Works with Twilio TEST credentials (the sandbox) as well as live ones.
//
// Config keys (stored in integrations.config_json):
//   accountSid  — Twilio Account SID (ACxxxx…)   [not secret, but scoped]
//   authToken   — Twilio Auth Token              [SENSITIVE — encrypted at rest]
//   fromNumber  — sender number (for send-test)  [optional]
public sealed class TwilioConnector(IHttpClientFactory httpFactory, ILogger<TwilioConnector> logger) : IConnector
{
    public IReadOnlyCollection<string> Keys { get; } = new[] { "twilio-sms" };
    public string DisplayName => "Twilio SMS";

    private HttpClient Client()
    {
        var c = httpFactory.CreateClient("twilio");
        c.BaseAddress ??= new Uri("https://api.twilio.com/");
        c.Timeout = TimeSpan.FromSeconds(12);
        return c;
    }

    private static AuthenticationHeaderValue BasicAuth(string sid, string token)
        => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{token}")));

    public async Task<ConnectorResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string?> config, CancellationToken ct)
    {
        // Twilio supports two credential styles — we accept both:
        //   • Account SID (AC…) + Auth Token
        //   • API Key SID (SK…) + Secret  (basic-auth username:password)
        // For an API key we validate by LISTING accounts (works with key auth); for an
        // Account SID we fetch that account directly.
        var accountSid = config.GetValueOrDefault("accountSid");
        var authToken = config.GetValueOrDefault("authToken");
        var apiKeySid = config.GetValueOrDefault("apiKeySid") ?? config.GetValueOrDefault("sid");
        var apiKeySecret = config.GetValueOrDefault("apiKeySecret") ?? config.GetValueOrDefault("secret");

        string user, pass, path;
        if (!string.IsNullOrWhiteSpace(apiKeySid) && apiKeySid!.StartsWith("SK", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(apiKeySecret))
        {
            user = apiKeySid!; pass = apiKeySecret!;
            // API keys authenticate list endpoints; validates the key + secret are live.
            path = "2010-04-01/Accounts.json?PageSize=1";
        }
        else if (!string.IsNullOrWhiteSpace(accountSid) && !string.IsNullOrWhiteSpace(authToken))
        {
            user = accountSid!; pass = authToken!;
            path = $"2010-04-01/Accounts/{Uri.EscapeDataString(accountSid!)}.json";
        }
        else
        {
            return ConnectorResult.Fail("Add Twilio credentials in Configure: either an Account SID + Auth Token, or an API Key SID (SK…) + Secret.");
        }

        try
        {
            var client = Client();
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = BasicAuth(user, pass);
            using var resp = await client.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode)
            {
                string? friendly = null, status = null;
                try
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    var root = doc.RootElement;
                    // list endpoint wraps in { accounts: [ {…} ] }
                    if (root.TryGetProperty("accounts", out var accts) && accts.ValueKind == JsonValueKind.Array && accts.GetArrayLength() > 0)
                        root = accts[0];
                    friendly = root.TryGetProperty("friendly_name", out var fn) ? fn.GetString() : null;
                    status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
                }
                catch { /* body optional */ }
                return ConnectorResult.Ok(
                    $"Connected to Twilio{(friendly is null ? "" : $" — account \"{friendly}\"")}.",
                    new Dictionary<string, object?> { ["accountStatus"] = status, ["friendlyName"] = friendly });
            }

            if ((int)resp.StatusCode == 401)
                return ConnectorResult.Fail("Twilio rejected the credentials (401). Check the SID/token (or API Key SID + Secret).");
            return ConnectorResult.Fail($"Twilio returned {(int)resp.StatusCode} {resp.ReasonPhrase}.");
        }
        catch (TaskCanceledException)
        {
            return ConnectorResult.Fail("Twilio did not respond in time (timeout). Check network egress.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Twilio test-connection failed");
            return ConnectorResult.Fail($"Could not reach Twilio: {ex.Message}");
        }
    }

    public async Task<ConnectorResult> RunActionAsync(
        string action, IReadOnlyDictionary<string, string?> config, JsonElement? body, CancellationToken ct)
    {
        if (!string.Equals(action, "send-test", StringComparison.OrdinalIgnoreCase))
            return ConnectorResult.Fail($"Action '{action}' is not supported by Twilio SMS.");

        var sid = config.GetValueOrDefault("accountSid");
        var token = config.GetValueOrDefault("authToken");
        var from = config.GetValueOrDefault("fromNumber");
        var to = body.HasValue && body.Value.TryGetProperty("to", out var t) ? t.GetString() : null;
        var text = (body.HasValue && body.Value.TryGetProperty("body", out var b) ? b.GetString() : null)
                   ?? "OpsTrax test message — your Twilio connector is live.";

        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token))
            return ConnectorResult.Fail("Missing Twilio credentials.");
        if (string.IsNullOrWhiteSpace(from)) return ConnectorResult.Fail("Set a fromNumber in Configure to send a test SMS.");
        if (string.IsNullOrWhiteSpace(to)) return ConnectorResult.Fail("Provide a 'to' number to send the test SMS.");

        try
        {
            var client = Client();
            using var req = new HttpRequestMessage(HttpMethod.Post, $"2010-04-01/Accounts/{Uri.EscapeDataString(sid)}/Messages.json");
            req.Headers.Authorization = BasicAuth(sid!, token!);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["To"] = to!, ["From"] = from!, ["Body"] = text,
            });
            using var resp = await client.SendAsync(req, ct);
            var payload = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode)
                return ConnectorResult.Ok($"Test SMS accepted by Twilio for {to}.");
            return ConnectorResult.Fail($"Twilio rejected the message ({(int)resp.StatusCode}): {payload}");
        }
        catch (Exception ex)
        {
            return ConnectorResult.Fail($"Send failed: {ex.Message}");
        }
    }
}
