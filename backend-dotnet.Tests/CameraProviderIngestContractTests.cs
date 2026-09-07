using System.Text;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class CameraProviderIngestContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static CameraProviderEventEnvelope Event(
        IReadOnlyList<CameraProviderMediaReference>? media = null,
        string provider = "Samsara",
        string eventId = "evt-001",
        DateTimeOffset? occurred = null,
        DateTimeOffset? received = null) => new(
            provider, "account-001", eventId, "v1", "HarshBraking",
            occurred ?? Now.AddMinutes(-2), received ?? Now.AddMinutes(-1),
            null, null, null, null, "vehicle-external-1", null, null,
            media ?? []);

    private static CameraProviderMediaReference Media(
        string mediaId = "opaque-media-1",
        DateTimeOffset? expires = null) => new(
            "RoadFacing", "Video", "video/mp4", mediaId, Now.AddMinutes(-2), 15_000,
            expires ?? Now.AddHours(1), "Triggered", "Safety30Days", "privacy-v1");

    [Fact]
    public void Prepare_NormalizesProviderAndHashesExactAuthenticatedPayload()
    {
        var payload = Encoding.UTF8.GetBytes("{\"event\":\"evt-001\"}");

        var prepared = CameraProviderIngestService.Prepare(Event([Media()]), payload, Now);

        Assert.Equal("samsara", prepared.ProviderKey);
        Assert.Equal("34bf96c4057a28872ed43ad4e236f69f2e5eda9a368206415c9d3930c4d6fbb7", prepared.PayloadSha256);
        Assert.Equal("ProviderPending", prepared.MediaReferences.Single().RetrievalStatus);
        Assert.Null(prepared.DriverId);
    }

    [Fact]
    public void Prepare_MarksExpiredOpaqueMediaWithoutPromotingAccess()
    {
        var prepared = CameraProviderIngestService.Prepare(
            Event([Media(expires: Now.AddSeconds(-1))]), new byte[] { 1 }, Now);

        Assert.Equal("Expired", prepared.MediaReferences.Single().RetrievalStatus);
        Assert.Equal("ExternalHold", CameraProviderIngestService.ExternalHold);
    }

    [Theory]
    [InlineData("https://provider.example/video?id=1")]
    [InlineData("opaque-media?signature=secret")]
    [InlineData("opaque-media#fragment")]
    public void Prepare_RefusesUrlOrSignedLinkAsMediaIdentity(string mediaId)
    {
        var error = Assert.Throws<CameraProviderEventValidationException>(() =>
            CameraProviderIngestService.Prepare(Event([Media(mediaId)]), new byte[] { 1 }, Now));

        Assert.Contains("opaque identifiers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepare_RefusesDuplicateMediaIdentityAndNonUtcOrFutureClocks()
    {
        Assert.Throws<CameraProviderEventValidationException>(() =>
            CameraProviderIngestService.Prepare(Event([Media(), Media()]), new byte[] { 1 }, Now));
        Assert.Throws<CameraProviderEventValidationException>(() =>
            CameraProviderIngestService.Prepare(
                Event(occurred: Now.ToOffset(TimeSpan.FromHours(1))), new byte[] { 1 }, Now));
        Assert.Throws<CameraProviderEventValidationException>(() =>
            CameraProviderIngestService.Prepare(
                Event(received: Now.AddMinutes(6)), new byte[] { 1 }, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2097153)]
    public void Prepare_RefusesEmptyOrOversizedPayload(int size)
    {
        Assert.Throws<CameraProviderEventValidationException>(() =>
            CameraProviderIngestService.Prepare(Event(), new byte[size], Now));
    }

    [Fact]
    public void Stage112_IsSystemOnlyForceRlsAndPermanentlyExternalHold()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var sql = File.ReadAllText(Path.Combine(root, "database", "migrations", "2026_09_07_stage112_camera_provider_ingest_spine.sql"));
        var runner = File.ReadAllText(Path.Combine(root, "tools", "apply-neon-predeploy-migrations.sh"));
        var service = File.ReadAllText(Path.Combine(root, "backend-dotnet", "Services", "CameraProviderIngestService.cs"));

        Assert.Contains("camera_provider_event_inbox FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("camera_provider_media_references FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
        Assert.Contains("REVOKE ALL ON TABLE camera_provider_event_inbox,camera_provider_media_references FROM opstrax_app", sql, StringComparison.Ordinal);
        Assert.Contains("provider_verification_status='ExternalHold'", sql, StringComparison.Ordinal);
        Assert.Contains("access_status='ExternalHold'", sql, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (company_id,provider_key,provider_account_ref,provider_event_id)", sql, StringComparison.Ordinal);
        Assert.Contains("ck_camera_provider_media_deletion_order", sql, StringComparison.Ordinal);
        Assert.Contains("ck_camera_provider_event_identity_immutable", sql, StringComparison.Ordinal);
        Assert.Contains("ck_camera_provider_media_identity_immutable", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("source_authority='Authoritative'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO dashcam_events", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026_09_07_stage112_camera_provider_ingest_spine", runner, StringComparison.Ordinal);
        Assert.Contains("Stage112 camera provider evidence escaped ExternalHold", runner, StringComparison.Ordinal);
    }
}
