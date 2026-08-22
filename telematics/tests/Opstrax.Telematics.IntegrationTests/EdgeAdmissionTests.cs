using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Telematics.Gateway.Edge;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Covers the first gate on the public port: which IMEIs are allowed to open a session at all.
/// The failure that matters here is <b>failing open</b> — an allowlist that admits everything
/// because its file went missing turns a provisioned edge back into an open relay.
/// </summary>
public sealed class EdgeAdmissionTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory("opstrax-allowlist-").FullName;

    private ImeiAllowlist Build(AllowlistOptions options, Func<DateTime>? clock = null) =>
        new(options, NullLogger.Instance, clock);

    [Fact]
    public void EmptyAllowlist_AdmitsNothing()
    {
        ImeiAllowlist allowlist = Build(new AllowlistOptions());

        Assert.False(allowlist.IsAllowed("862464068456321"));
        Assert.False(allowlist.IsAllowed(null));
        Assert.False(allowlist.IsAllowed(""));
        Assert.Equal(0, allowlist.Count);
    }

    [Fact]
    public void InlineImeis_AreAdmitted_AndOthersAreNot()
    {
        ImeiAllowlist allowlist = Build(new AllowlistOptions
        {
            Imeis = { "862464068456321" },
        });

        Assert.True(allowlist.IsAllowed("862464068456321"));
        Assert.False(allowlist.IsAllowed("862464068456322"));
    }

    [Theory]
    [InlineData("862464068456321")]
    [InlineData("86-246406-845632-1")]
    [InlineData(" 862464068456321 ")]
    [InlineData("862 464 068 456 321")]
    public void OperatorFormatting_IsCanonicalised(string configured)
    {
        // Operators paste IMEIs out of spreadsheets and shipping manifests, where separators are
        // normal. A device that reports bare digits must still match.
        ImeiAllowlist allowlist = Build(new AllowlistOptions { Imeis = { configured } });

        Assert.True(allowlist.IsAllowed("862464068456321"));
    }

    [Theory]
    [InlineData("86246406845632X")]   // a stray non-separator character
    [InlineData("1234567")]            // too short to be an identifier
    public void NonIdentifierEntries_AreRejectedRatherThanCoerced(string configured)
    {
        ImeiAllowlist allowlist = Build(new AllowlistOptions { Imeis = { configured } });

        Assert.Equal(0, allowlist.Count);
    }

    [Fact]
    public void FileEntries_SupportCommentsAndInlineAnnotations()
    {
        string path = Path.Combine(_directory, "allowlist.txt");
        File.WriteAllText(path, string.Join('\n', new[]
        {
            "# Khalid pilot fleet",
            "862464068456321  # PT40-Q, tractor 118",
            "",
            "864000000000007\t\t# spare unit",
            "   # trailing comment only",
        }));

        ImeiAllowlist allowlist = Build(new AllowlistOptions { Path = path });

        Assert.True(allowlist.IsAllowed("862464068456321"));
        Assert.True(allowlist.IsAllowed("864000000000007"));
        Assert.Equal(2, allowlist.Count);
    }

    [Fact]
    public void FileAndInlineEntries_AreMerged()
    {
        string path = Path.Combine(_directory, "allowlist.txt");
        File.WriteAllText(path, "864000000000007\n");

        ImeiAllowlist allowlist = Build(new AllowlistOptions
        {
            Path = path,
            Imeis = { "862464068456321" },
        });

        Assert.True(allowlist.IsAllowed("862464068456321"));
        Assert.True(allowlist.IsAllowed("864000000000007"));
    }

    [Fact]
    public void MissingFile_FailsClosed_AndKeepsOnlyInlineEntries()
    {
        ImeiAllowlist allowlist = Build(new AllowlistOptions
        {
            Path = Path.Combine(_directory, "does-not-exist.txt"),
            Imeis = { "862464068456321" },
        });

        Assert.True(allowlist.IsFileFaulted);
        Assert.False(allowlist.IsAllowed("864000000000007"));
        Assert.True(allowlist.IsAllowed("862464068456321"));
    }

    [Fact]
    public void DeletedFile_StopsAdmittingItsEntries_RatherThanServingStaleContents()
    {
        // The revocation case: an operator deletes or replaces the file to pull a device. Serving
        // the last good contents would keep a revoked unit connected indefinitely.
        string path = Path.Combine(_directory, "allowlist.txt");
        File.WriteAllText(path, "862464068456321\n");

        DateTime now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        ImeiAllowlist allowlist = Build(new AllowlistOptions
        {
            Path = path,
            ReloadInterval = TimeSpan.FromSeconds(15),
        }, () => now);

        Assert.True(allowlist.IsAllowed("862464068456321"));

        File.Delete(path);
        now = now.AddMinutes(1);

        Assert.False(allowlist.IsAllowed("862464068456321"));
        Assert.True(allowlist.IsFileFaulted);
    }

    [Fact]
    public void NewlyCommissionedDevice_IsAdmittedWithoutARestart()
    {
        string path = Path.Combine(_directory, "allowlist.txt");
        File.WriteAllText(path, "862464068456321\n");

        DateTime now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        ImeiAllowlist allowlist = Build(new AllowlistOptions
        {
            Path = path,
            ReloadInterval = TimeSpan.FromSeconds(15),
        }, () => now);

        Assert.False(allowlist.IsAllowed("864000000000007"));

        File.WriteAllText(path, "862464068456321\n864000000000007\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        now = now.AddMinutes(1);

        Assert.True(allowlist.IsAllowed("864000000000007"));
        Assert.False(allowlist.IsFileFaulted);
    }

    [Fact]
    public void ReloadInterval_BoundsFileChecks()
    {
        string path = Path.Combine(_directory, "allowlist.txt");
        File.WriteAllText(path, "862464068456321\n");

        DateTime now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        ImeiAllowlist allowlist = Build(new AllowlistOptions
        {
            Path = path,
            ReloadInterval = TimeSpan.FromMinutes(10),
        }, () => now);

        File.WriteAllText(path, "862464068456321\n864000000000007\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        // Inside the interval the new entry is not yet visible: the point of the bound is that a
        // connection flood cannot turn into a stat() flood.
        now = now.AddSeconds(30);
        Assert.False(allowlist.IsAllowed("864000000000007"));

        now = now.AddMinutes(11);
        Assert.True(allowlist.IsAllowed("864000000000007"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}
