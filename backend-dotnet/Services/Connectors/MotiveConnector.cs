using System.Net.Http.Headers;
using System.Globalization;
using System.Text.Json;

namespace Opstrax.Api.Services.Connectors;

/// <summary>
/// Minimal read-only Motive adapter for the controlled G2B evidence lane. A
/// connection passes only when Motive accepts the OAuth token for every required
/// company, vehicle, ELD, location, HOS, and inspection-report read scope.
/// </summary>
public sealed class MotiveConnector(
    IHttpClientFactory httpFactory,
    ILogger<MotiveConnector> logger) : IConnector
{
    public IReadOnlyCollection<string> Keys { get; } = ["motive"];
    public string DisplayName => "Motive";

    internal const string BaseUrl = "https://api.gomotive.com";

    private static readonly (string Path, string Scope, string Label)[] RequiredProbes =
    [
        ("/v1/companies", "companies.read", "company identity"),
        ("/v1/users?per_page=1", "users.read", "drivers and fleet managers"),
        ("/v1/vehicles?per_page=1", "vehicles.read", "vehicles"),
        ("/v1/eld_devices?per_page=1", "eld_devices.read", "ELD devices"),
        ("/v1/vehicle_locations?per_page=1", "locations.vehicle_locations_list", "current vehicle locations"),
        ("/v1/hours_of_service?per_page=1&start_date={utcDate}&end_date={utcDate}", "hos_logs.hours_of_service", "hours of service"),
        ("/v1/hos_violations?per_page=1", "hos_logs.hos_violation", "HOS violations"),
        ("/v1/logs?per_page=1", "hos_logs.logs", "HOS logs"),
        ("/v1/inspection_reports?per_page=1", "inspection_reports.read", "inspection reports"),
    ];

    public async Task<ConnectorResult> TestConnectionAsync(
        IReadOnlyDictionary<string, string?> config, CancellationToken ct)
    {
        var accessToken = config.GetValueOrDefault("accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
            return ConnectorResult.Fail("Authorize this tenant through Motive OAuth before testing the connection.");
        if (!DateTimeOffset.TryParse(config.GetValueOrDefault("tokenExpiresAt"), out var expiresAt)
            || expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            return ConnectorResult.Fail("The Motive access token is expired or has no verified expiry. Reauthorize the tenant before testing.");

        try
        {
            var client = httpFactory.CreateClient("motive");
            client.BaseAddress = new Uri(BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var verificationTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Leave completion budget below the 30-second UI timeout and
            // 45-second integration-operation lease.
            verificationTimeout.CancelAfter(TimeSpan.FromSeconds(25));
            var utcDate = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var probe in RequiredProbes)
            {
                var probePath = probe.Path.Replace("{utcDate}", utcDate, StringComparison.Ordinal);
                using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(verificationTimeout.Token);
                probeTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var response = await client.GetAsync(probePath, HttpCompletionOption.ResponseHeadersRead, probeTimeout.Token);
                if ((int)response.StatusCode is 401 or 403)
                    return ConnectorResult.Fail(
                        $"Motive rejected the OAuth token or required read scope {probe.Scope} for {probe.Label}.");
                if ((int)response.StatusCode == 429)
                    return ConnectorResult.Fail("Motive rate-limited the verification request. Retry after the provider window resets.");
                if (!response.IsSuccessStatusCode)
                    return ConnectorResult.Fail(
                        $"Motive {probe.Label} verification returned HTTP {(int)response.StatusCode}.");

                try
                {
                    using var document = await MotiveResponseReader.ReadJsonAsync(
                        response.Content, MotiveResponseReader.ProbeResponseBytes, probeTimeout.Token);
                    if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                        return ConnectorResult.Fail($"Motive {probe.Label} returned an invalid JSON envelope.");
                }
                catch (JsonException)
                {
                    return ConnectorResult.Fail($"Motive {probe.Label} returned malformed JSON.");
                }
            }

            return ConnectorResult.Ok(
                "Connected to Motive — all nine required read-only endpoint probes passed. This does not certify data sync or ELD compliance.",
                new Dictionary<string, object?>
                {
                    ["verifiedEndpointCount"] = RequiredProbes.Length,
                    ["verifiedScopes"] = RequiredProbes.Select(item => item.Scope).ToArray(),
                    ["writeScopesRequested"] = false,
                });
        }
        catch (MotiveResponseReader.ResponseTooLargeException)
        {
            return ConnectorResult.Fail("Motive verification response exceeded the allowed size.");
        }
        catch (OperationCanceledException)
        {
            return ConnectorResult.Fail("Motive did not respond in time.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Motive connection verification failed.");
            return ConnectorResult.Fail("Motive connection verification could not be completed.");
        }
    }
}
