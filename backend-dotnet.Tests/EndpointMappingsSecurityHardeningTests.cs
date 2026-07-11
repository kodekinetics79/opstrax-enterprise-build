using Microsoft.AspNetCore.Http;
using Opstrax.Api;
using Opstrax.Api.Controllers;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class EndpointMappingsSecurityHardeningTests
{
    [Fact]
    public void Login_UsesLockoutAndSecurityEventsBeforeSessionIssuance()
    {
        var login = MethodSource("Login(", "private static IResult InvalidCredentials");

        AssertOrdered(login, "CheckLockoutAsync", "VerifyPasswordHash");
        AssertOrdered(login, "userStatus", "VerifyPasswordHash");
        AssertOrdered(login, "RecordFailedLoginAsync", "RecordSuccessfulLoginAsync");
        AssertOrdered(login, "RecordSuccessfulLoginAsync", "var token =");
        Assert.Contains("return InvalidCredentials();", login, StringComparison.Ordinal);
        Assert.DoesNotContain("Account unavailable", login, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_DoesNotIssueTokenWhenSessionPersistenceFails()
    {
        var login = MethodSource("Login(", "private static IResult InvalidCredentials");

        Assert.Contains("INSERT INTO user_sessions", login, StringComparison.Ordinal);
        Assert.DoesNotContain("SESSION-INSERT-FAIL", login, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (Exception _sessionEx)", login, StringComparison.Ordinal);
    }

    [Fact]
    public void TelemetryIngest_RequiresSignatureAndStoredSecret()
    {
        var ingest = MethodSource("TelemetryIngest(", "// 9. Secondary body EventTime check");

        AssertOrdered(ingest, "string.IsNullOrWhiteSpace(xSig)", "Look up device");
        Assert.Contains("string.IsNullOrWhiteSpace(hmacSecret)", ingest, StringComparison.Ordinal);
        Assert.Contains("Device credentials are incomplete", ingest, StringComparison.Ordinal);
        Assert.DoesNotContain("skip HMAC", ingest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TelemetryHmac_WrongSignatureFailsConstantTimeComparison()
    {
        var signature = TelemetryHmacHelper.ComputeSignature(
            "test-secret"u8.ToArray(), "POST", "/api/telemetry/ingest",
            "1700000000", "nonce-1", TelemetryHmacHelper.Sha256Hex("{}"));

        Assert.True(TelemetryHmacHelper.ConstantTimeEquals(signature, signature));
        Assert.False(TelemetryHmacHelper.ConstantTimeEquals(signature, new string('0', signature.Length)));
        Assert.False(TelemetryHmacHelper.ConstantTimeEquals(signature, string.Empty));
    }

    [Theory]
    [InlineData("1700000000", 1700000000L)]
    [InlineData("1700000000000", 1700000000L)]
    [InlineData("2023-11-14T22:13:20Z", 1700000000L)]
    public void TrackerTimestamp_ParsesSecondsMillisecondsAndIso8601(string raw, long expectedSeconds)
    {
        Assert.True(EndpointMappings.TryParseTrackerTimestamp(raw, out var parsed));
        Assert.Equal(expectedSeconds, parsed.ToUnixTimeSeconds());
    }

    [Fact]
    public void GpsGatewayIngest_UsesGatewayAsCredentialAndDoesNotReturnDeviceIdentifiers()
    {
        var ingest = MethodSource("GpsTrackerIngest(", "// ── GET /api/telemetry/metrics");

        Assert.Contains("Telemetry:GatewaySecret", ingest, StringComparison.Ordinal);
        Assert.Contains("FixedTimeEquals", ingest, StringComparison.Ordinal);
        Assert.Contains("last_heartbeat_at=NOW()", ingest, StringComparison.Ordinal);
        Assert.Contains("latest_vehicle_positions.event_time <= EXCLUDED.event_time", ingest, StringComparison.Ordinal);
        Assert.DoesNotContain("new { imei", ingest, StringComparison.Ordinal);
    }

    [Fact]
    public void AiFallback_DoesNotExposeExceptionMessages()
    {
        var aiAsk = MethodSource("AiAsk(", "private static Func<HttpContext");

        Assert.DoesNotContain("ex.Message", aiAsk, StringComparison.Ordinal);
        Assert.Contains("AI service temporarily unavailable.", aiAsk, StringComparison.Ordinal);
    }

    [Fact]
    public void SensitiveReadFamilies_RequirePermissionsAndTenantPredicates()
    {
        var source = Source();

        Assert.Contains("RequirePermission(http, \"dashcam:view\")", source, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, \"safety:evidence:view\")", source, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, \"reports:view\")", source, StringComparison.Ordinal);
        Assert.Contains("WHERE de.id=@id AND de.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE se.id=@id AND se.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE ep.id=@id AND ep.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE tenant_id=@cid ORDER BY snapshot_date", source, StringComparison.Ordinal);
        Assert.Contains("WHERE company_id=@cid AND entity_name=@entity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionGuard_DeniesMissingSensitivePermission()
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthRoleItemKey] = "Driver";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = Array.Empty<string>();

        Assert.NotNull(EndpointMappings.RequirePermission(http, "safety:evidence:view"));
    }

    [Fact]
    public void TenantHelpersAndEvidenceReferences_RemainFailClosed()
    {
        var source = Source();

        Assert.DoesNotContain("await ModuleRecommendations(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await AuditRows(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Task<List<Dictionary<string, object?>>> ModuleRecommendations(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Task<List<Dictionary<string, object?>>> AuditRows(", source, StringComparison.Ordinal);

        var createEvidence = SourceBlock(
            source,
            "private static async Task<IResult> CreateEvidencePackage(",
            "private static async Task<IResult> UpdateEvidencePackage(");
        Assert.Contains("incidents WHERE id=@incident AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("safety_events WHERE id=@safety AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("dashcam_events WHERE id=@dashcam AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("drivers WHERE id=@driver AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("vehicles WHERE id=@vehicle AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("jobs WHERE id=@job AND company_id=@cid", createEvidence, StringComparison.Ordinal);
    }

    private static void AssertOrdered(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Missing expected marker: {first}");
        Assert.True(secondIndex >= 0, $"Missing expected marker: {second}");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'");
    }

    private static string MethodSource(string startMarker, string endMarker)
    {
        var source = Source();
        return SourceBlock(source, startMarker, endMarker);
    }

    private static string SourceBlock(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to locate source block {startMarker}");
        return source[start..end];
    }

    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    }
}
