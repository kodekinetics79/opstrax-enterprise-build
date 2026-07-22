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
        var body = source[start..(start + 1200)];

        // Server-generated with cryptographic entropy...
        Assert.Contains("RandomNumberGenerator.GetBytes(32)", body, StringComparison.Ordinal);
        // ...and must NOT fall back to a caller-supplied token.
        Assert.DoesNotContain("req.Token", body, StringComparison.Ordinal);
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
