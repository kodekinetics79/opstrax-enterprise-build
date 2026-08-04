namespace Opstrax.Tests;

// P2 security fix guard — public tracking tokens must be server-generated with enforced entropy, never
// accepted from the caller. A caller-supplied token could be short/sequential/guessable, undermining the
// unguessable-secret guarantee that gates anonymous shipment visibility.
public sealed class FleetTmsTrackingTokenTests
{
    [Fact]
    public void CreateTrackingLink_Generates_Token_And_Ignores_Caller_Supplied_Value()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "FleetTmsEndpoints.cs");
        var start = source.IndexOf("private static async Task<IResult> CreateTrackingLink(", StringComparison.Ordinal);
        Assert.True(start >= 0, "CreateTrackingLink must exist");
        var end = source.IndexOf("private static async Task<IResult> RevokeTrackingLink(", start, StringComparison.Ordinal);
        Assert.True(end > start, "CreateTrackingLink method boundary must exist");
        var body = source[start..end];

        // Server-generated with cryptographic entropy...
        Assert.Contains("RandomNumberGenerator.GetBytes(32)", body, StringComparison.Ordinal);
        // ...and must NOT fall back to a caller-supplied token.
        Assert.DoesNotContain("req.Token", body, StringComparison.Ordinal);
        Assert.Contains("HashTrackingToken(token)", body, StringComparison.Ordinal);
        Assert.Contains("token_hash", body, StringComparison.Ordinal);
        Assert.Contains("created[\"token\"] = token", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Tracking_Token_Hash_Uses_Stable_Sha256()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            Opstrax.Api.Controllers.FleetTmsEndpoints.HashTrackingToken("abc"));
    }

    [Fact]
    public void List_And_Public_Resolution_Do_Not_Read_Plaintext_Tokens()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "FleetTmsEndpoints.cs");
        var listStart = source.IndexOf("private static async Task<IResult> GetTrackingLinks(", StringComparison.Ordinal);
        var createStart = source.IndexOf("private static async Task<IResult> CreateTrackingLink(", StringComparison.Ordinal);
        var listBody = source[listStart..createStart];
        Assert.DoesNotContain("SELECT *", listBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token_hash", listBody, StringComparison.Ordinal);

        var resolveStart = source.IndexOf("private static async Task<Dictionary<string, object?>?> ResolveLink(", StringComparison.Ordinal);
        var publicStart = source.IndexOf("private static async Task<IResult> PublicTrack(", resolveStart, StringComparison.Ordinal);
        var resolveBody = source[resolveStart..publicStart];
        Assert.Contains("token_hash=@tokenHash", resolveBody, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE token=@token", resolveBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_Migration_Backfills_Hashes_Then_Clears_Plaintext()
    {
        var source = ReadSource("backend-dotnet", "Services", "FleetTmsSchemaService.cs");
        Assert.Contains("ADD COLUMN IF NOT EXISTS token_hash", source, StringComparison.Ordinal);
        Assert.Contains("sha256(convert_to(token, 'UTF8'))", source, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN token_hash SET NOT NULL", source, StringComparison.Ordinal);
        Assert.Contains("SET token = NULL", source, StringComparison.Ordinal);
        Assert.Contains("idx_ftms_links_token_hash", source, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
