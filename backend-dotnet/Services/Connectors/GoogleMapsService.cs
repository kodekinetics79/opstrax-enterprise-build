using System.Text.Json;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services.Connectors;

// Server-side Google Maps capability: geocoding, directions (routing), and ETA/
// distance. The map itself stays on free Leaflet/OpenStreetMap tiles — Google is used
// only where Leaflet is weak and only server-side, so the API key is never exposed to
// the browser. The key is read (decrypted) from the tenant's google-maps-platform
// connector config, so each tenant uses its own key and billing.
public sealed class GoogleMapsService(
    IHttpClientFactory httpFactory,
    ConnectorRegistry connectors,
    ILogger<GoogleMapsService> logger)
{
    public sealed record GeoResult(bool Ok, string Message, double? Lat = null, double? Lng = null, string? FormattedAddress = null);
    public sealed record RouteResult(bool Ok, string Message, double? DistanceMeters = null, double? DurationSeconds = null, string? Polyline = null, string? Summary = null);

    // Resolve the tenant's Google Maps API key from the stored connector config
    // (decrypted). Returns null when the connector is not configured for this tenant.
    public async Task<string?> ResolveKeyAsync(Database db, long companyId, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            "SELECT config_json FROM integrations WHERE company_id=@cid AND integration_key='google-maps-platform' LIMIT 1",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);
        if (row is null) return null;
        var cfg = connectors.DecryptConfig(row.GetValueOrDefault("configJson"));
        return cfg.GetValueOrDefault("apiKey");
    }

    public async Task<GeoResult> GeocodeAsync(string apiKey, string address, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address)) return new GeoResult(false, "Address is required.");
        try
        {
            var client = httpFactory.CreateClient("connector-http");
            client.Timeout = TimeSpan.FromSeconds(12);
            var url = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(address)}&key={Uri.EscapeDataString(apiKey)}";
            using var resp = await client.GetAsync(url, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "UNKNOWN";
            if (status != "OK")
            {
                var msg = doc.RootElement.TryGetProperty("error_message", out var em) ? em.GetString() : status;
                return new GeoResult(false, $"Geocode failed ({status}): {msg}");
            }
            var result = doc.RootElement.GetProperty("results")[0];
            var loc = result.GetProperty("geometry").GetProperty("location");
            return new GeoResult(true, "OK",
                loc.GetProperty("lat").GetDouble(),
                loc.GetProperty("lng").GetDouble(),
                result.TryGetProperty("formatted_address", out var fa) ? fa.GetString() : null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Geocode failed for {Address}", address);
            return new GeoResult(false, $"Geocode error: {ex.Message}");
        }
    }

    public async Task<RouteResult> DirectionsAsync(string apiKey, string origin, string destination, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination))
            return new RouteResult(false, "origin and destination are required.");
        try
        {
            var client = httpFactory.CreateClient("connector-http");
            client.Timeout = TimeSpan.FromSeconds(12);
            var url = $"https://maps.googleapis.com/maps/api/directions/json?origin={Uri.EscapeDataString(origin)}&destination={Uri.EscapeDataString(destination)}&key={Uri.EscapeDataString(apiKey)}";
            using var resp = await client.GetAsync(url, ct);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "UNKNOWN";
            if (status != "OK")
            {
                var msg = doc.RootElement.TryGetProperty("error_message", out var em) ? em.GetString() : status;
                return new RouteResult(false, $"Directions failed ({status}): {msg}");
            }
            var route = doc.RootElement.GetProperty("routes")[0];
            var leg = route.GetProperty("legs")[0];
            return new RouteResult(true, "OK",
                leg.GetProperty("distance").GetProperty("value").GetDouble(),
                leg.GetProperty("duration").GetProperty("value").GetDouble(),
                route.TryGetProperty("overview_polyline", out var op) && op.TryGetProperty("points", out var pts) ? pts.GetString() : null,
                route.TryGetProperty("summary", out var sm) ? sm.GetString() : null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Directions failed {O}->{D}", origin, destination);
            return new RouteResult(false, $"Directions error: {ex.Message}");
        }
    }
}
