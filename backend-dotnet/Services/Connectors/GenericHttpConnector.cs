using System.Net.Http.Headers;
using System.Text.Json;

namespace Opstrax.Api.Services.Connectors;

// Generic HTTP / Webhook connector — the fallback for ANY connector (including
// user-created custom ones) that exposes an HTTP endpoint. TestConnection issues a
// real request to the configured URL and reports the actual reachability + status,
// so even a custom connector you add is genuinely testable against its live system.
//
// Config keys (any that are present are used):
//   baseUrl | webhookUrl | url  — endpoint to probe (required)
//   apiKey | token              — bearer/api-key credential [SENSITIVE — encrypted]
//   authHeader                  — header name for the key (default "Authorization")
//   authScheme                  — scheme prefix (default "Bearer"; use "" for raw key)
//   method                      — HEAD | GET | POST (default GET)
//
// This is the registry's DEFAULT (it has no fixed Keys) — the registry falls back to
// it when no provider-specific connector matches the integration_key.
public sealed class GenericHttpConnector(IHttpClientFactory httpFactory, ILogger<GenericHttpConnector> logger) : IConnector
{
    // Empty — this is resolved as the fallback, not by key match.
    public IReadOnlyCollection<string> Keys { get; } = Array.Empty<string>();
    public string DisplayName => "HTTP / Webhook";

    public async Task<ConnectorResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string?> config, CancellationToken ct)
    {
        var url = config.GetValueOrDefault("baseUrl")
                  ?? config.GetValueOrDefault("webhookUrl")
                  ?? config.GetValueOrDefault("url");
        if (string.IsNullOrWhiteSpace(url))
            return ConnectorResult.Fail("No endpoint configured. Add a baseUrl, webhookUrl, or url in Configure, then test again.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            return ConnectorResult.Fail($"'{url}' is not a valid http(s) URL.");

        var methodName = (config.GetValueOrDefault("method") ?? "GET").ToUpperInvariant();
        var method = methodName switch { "HEAD" => HttpMethod.Head, "POST" => HttpMethod.Post, _ => HttpMethod.Get };
        var key = config.GetValueOrDefault("apiKey") ?? config.GetValueOrDefault("token");
        var headerName = config.GetValueOrDefault("authHeader");
        var scheme = config.GetValueOrDefault("authScheme") ?? "Bearer";

        try
        {
            var client = httpFactory.CreateClient("connector-http");
            client.Timeout = TimeSpan.FromSeconds(12);
            using var req = new HttpRequestMessage(method, uri);
            if (!string.IsNullOrWhiteSpace(key))
            {
                if (!string.IsNullOrWhiteSpace(headerName) && !headerName!.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    req.Headers.TryAddWithoutValidation(headerName!, key);
                else
                    req.Headers.Authorization = string.IsNullOrEmpty(scheme)
                        ? new AuthenticationHeaderValue(key!)
                        : new AuthenticationHeaderValue(scheme, key);
            }
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            var code = (int)resp.StatusCode;
            var details = new Dictionary<string, object?> { ["status"] = code, ["reachedHost"] = uri.Host };

            // 2xx/3xx = reachable & (if key sent) authorized. 401/403 = reachable but
            // rejected the credential. 404/405 = reachable but wrong path/method. Any
            // response at all proves the endpoint is live; auth failures are surfaced honestly.
            if (resp.IsSuccessStatusCode)
                return ConnectorResult.Ok($"Endpoint reachable — {code} {resp.ReasonPhrase} from {uri.Host}.", details);
            if (code is 401 or 403)
                return ConnectorResult.Fail($"Reached {uri.Host} but it rejected the credential ({code}). Check the API key/header.", details);
            return ConnectorResult.Fail($"Reached {uri.Host} but got {code} {resp.ReasonPhrase}. Check the URL/method.", details);
        }
        catch (TaskCanceledException)
        {
            return ConnectorResult.Fail($"No response from {uri.Host} in time (timeout).");
        }
        catch (HttpRequestException ex)
        {
            return ConnectorResult.Fail($"Could not reach {uri.Host}: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Generic HTTP test-connection failed for {Url}", url);
            return ConnectorResult.Fail($"Connection failed: {ex.Message}");
        }
    }
}
