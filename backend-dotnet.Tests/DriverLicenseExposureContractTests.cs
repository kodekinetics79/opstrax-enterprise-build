using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Security;
using Opstrax.Api.Services;
using Xunit;

namespace Opstrax.Tests;

/// <summary>
/// DEF-015 — driver license exposure. Display policy is "masked last-four": every
/// operational read surface renders "•••• 1234" (or "Unavailable"), never the enc:
/// ciphertext and never full plaintext. Full plaintext leaves the system ONLY via the
/// audited DSAR export (DataSubjectExport → ProjectDriverPii).
/// </summary>
public class DriverLicenseExposureContractTests
{
    // ── Masking unit contract ─────────────────────────────────────────────────────

    private static PiiProtectionService EnabledPii() =>
        new(new TestKeyProvider(), NullLogger<PiiProtectionService>.Instance);

    [Theory]
    [InlineData("D1234567", "•••• 4567")]
    [InlineData("  DL-99-1234  ", "•••• 1234")]
    [InlineData("123", "•••• 123")] // shorter than four: masked rendering of what exists
    public void MaskDriverLicense_RendersLastFourOnly(string plaintext, string expected)
    {
        var pii = EnabledPii();
        Assert.Equal(expected, EndpointMappings.MaskDriverLicense(plaintext, pii));
        // The same value stored encrypted must decrypt and mask identically.
        Assert.Equal(expected, EndpointMappings.MaskDriverLicense(pii.Encrypt(plaintext), pii));
    }

    [Fact]
    public void MaskDriverLicense_NeverEmitsCiphertextOrPlaintext()
    {
        var pii = EnabledPii();
        var encrypted = pii.Encrypt("SECRET-LICENSE-7788")!;
        var masked = EndpointMappings.MaskDriverLicense(encrypted, pii);

        Assert.DoesNotContain("enc:", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-LICENSE", masked, StringComparison.Ordinal);
        Assert.Equal("•••• 7788", masked);
    }

    [Fact]
    public void MaskDriverLicense_UndecryptableOrMissing_RendersUnavailable_NotCiphertext()
    {
        var pii = EnabledPii();
        // Envelope written under a key this process does not hold (keyId 9) → Decrypt → null.
        var foreignKey = new PiiProtectionService(new ForeignKeyProvider(), NullLogger<PiiProtectionService>.Instance);
        var undecryptable = foreignKey.Encrypt("CANNOT-READ-ME-0001")!;

        Assert.Equal(EndpointMappings.MaskedLicenseUnavailable, EndpointMappings.MaskDriverLicense(undecryptable, pii));
        Assert.Equal(EndpointMappings.MaskedLicenseUnavailable, EndpointMappings.MaskDriverLicense(null, pii));
        Assert.Equal(EndpointMappings.MaskedLicenseUnavailable, EndpointMappings.MaskDriverLicense(DBNull.Value, pii));
        Assert.Equal(EndpointMappings.MaskedLicenseUnavailable, EndpointMappings.MaskDriverLicense("   ", pii));
    }

    private sealed class ForeignKeyProvider : IDataKeyProvider
    {
        private readonly byte[] _key = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();
        public bool IsConfigured => true;
        public (byte KeyId, byte[] Key) ActiveKey => (9, _key);
        public byte[]? ResolveKey(byte keyId) => keyId == 9 ? _key : null;
        public byte[] IndexKey => new byte[32];
    }

    // ── Source contract: every license_number selection routes through the mask
    //    or the DSAR projection ────────────────────────────────────────────────────

    /// <summary>
    /// Enumerates every occurrence of "license_number" in EndpointMappings.cs and
    /// asserts its enclosing method is one of the known-legitimate sites: write paths
    /// (encrypt + blind index), masked read paths, or the DSAR export/erase pair.
    /// A new read surface selecting the license fails here until it masks.
    /// </summary>
    [Fact]
    public void EveryLicenseNumberSite_IsAWritePath_AMaskedReadPath_OrTheDsarPath()
    {
        var source = MappingsSource();
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "MapOpsTraxEndpoints",   // drivers/export registration — masked via transform (asserted below)
            "Drivers",               // roster — masked via transform (asserted below)
            "SafetyDriverScores",    // scorecards — MaskDriverLicenseIn (asserted below)
            "CreateDriver",          // write path (encrypt + blind index + duplicate check)
            "UpdateDriver",          // write path (mask round-trip guarded, asserted below)
            "DriversImportPreview",  // import write path
            "DriversImportCommit",   // import write path
            "LoadDriverImportIdentities", // private duplicate-validation lookup; never serialized
            "BindDriver",            // write-path binder (encrypts)
            "DataSubjectExport",     // DSAR — the ONLY full-plaintext exit
            "DataSubjectErase",      // crypto-shred write
        };

        var defs = Regex.Matches(source,
                @"(?:private|internal|public)\s+static\s+(?:async\s+)?[\w<>,\?\[\]\. ]+?\b(\w+)\s*\(")
            .Select(m => (Pos: m.Index, Name: m.Groups[1].Value))
            .OrderBy(d => d.Pos)
            .ToArray();

        foreach (Match hit in Regex.Matches(source, "license_number"))
        {
            var enclosing = defs.LastOrDefault(d => d.Pos < hit.Index).Name ?? "<file scope>";
            Assert.True(allowed.Contains(enclosing),
                $"license_number referenced in '{enclosing}' — new read surfaces must render " +
                "the masked form (MaskDriverLicense/MaskDriverLicenseIn); only the DSAR export " +
                "may return plaintext.");
        }
    }

    [Fact]
    public void MaskedSurfaces_AreActuallyWiredThroughTheMask()
    {
        var source = MappingsSource();

        // Roster + export wire the transform hook to the mask helper.
        var drivers = Block(source, "private static Task<IResult> Drivers(", "private static async Task<IResult> Customers(");
        Assert.Contains("transform: rows => MaskDriverLicenseIn(rows,", drivers, StringComparison.Ordinal);

        var export = Block(source, "app.MapGet(\"/api/drivers/export\"", "app.MapGet(\"/api/jobs/export\"");
        Assert.Contains("transform: rows => MaskDriverLicenseIn(rows,", export, StringComparison.Ordinal);

        // Scorecards mask in place.
        var scores = Block(source, "private static async Task<IResult> SafetyDriverScores(", "private static async Task<IResult> SafetyDashboard(");
        Assert.Contains("MaskDriverLicenseIn(scores,", scores, StringComparison.Ordinal);

        // Detail renders masked and no longer decrypts to plaintext.
        var detail = Block(source, "private static async Task<IResult> DriverDetail(", "private static async Task<IResult> CustomerDetail(");
        Assert.Contains("MaskDriverLicense(record.GetValueOrDefault(\"licenseNumber\")", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectDriverPii", detail, StringComparison.Ordinal);

        // Driver self-view DTO omits the license — the query must not fetch it either.
        var driverMe = Block(source, "private static async Task<IResult> DriverMe(", "private static async Task<IResult> DriverAssignments(");
        Assert.DoesNotContain("license_number", driverMe, StringComparison.Ordinal);

        // DSAR keeps the audited full-plaintext projection.
        var dsar = Block(source, "private static async Task<IResult> DataSubjectExport(", "private static async Task<IResult> DataSubjectErase(");
        Assert.Contains("ProjectDriverPii(subject,", dsar, StringComparison.Ordinal);

        // Update path refuses to store a round-tripped mask as the real license.
        var update = Block(source, "private static async Task<IResult> UpdateDriver(", "private static async Task<IResult> CreateCustomer(");
        Assert.Contains("licenseRaw.StartsWith(\"••••\"", update, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportingRegistry_MasksTheLicenseField_AndKeepsTheSensitiveGate()
    {
        var dataset = ReportingDatasetRegistry.Get("driver_safety_scores");
        Assert.NotNull(dataset);

        var license = dataset!.GetField("license_number");
        Assert.NotNull(license);
        Assert.True(license!.Sensitive, "license_number must stay behind the Sensitive gate");
        Assert.Equal("drivers:export", dataset.SensitivePermission);
        Assert.True(license.MaskPii, "license_number must render masked even WITH the sensitive grant");

        // drivers has no license_class column — the phantom field 42703'd every run.
        Assert.DoesNotContain("license_class", dataset.BaseQuery, StringComparison.Ordinal);
        Assert.Null(dataset.GetField("license_class"));

        // The P8 executors apply the mask post-query.
        var source = MappingsSource();
        var run = Block(source, "private static async Task<IResult> P8RunQuery(", "private static async Task<IResult> P8RunSavedReport(");
        Assert.Contains("MaskPiiDatasetFields(rows, dataset, http);", run, StringComparison.Ordinal);
        var csv = Block(source, "private static async Task<IResult> P8ExportCsv(", "private static async Task<IResult> P8ExportSavedReport(");
        Assert.Contains("MaskPiiDatasetFields(rows, dataset, http);", csv, StringComparison.Ordinal);
    }

    private static string Block(string source, string start, string end)
    {
        var s = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(s >= 0, $"marker not found: {start}");
        var e = source.IndexOf(end, s, StringComparison.Ordinal);
        Assert.True(e > s, $"end marker not found: {end}");
        return source[s..e];
    }

    private static string MappingsSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    }
}

/// <summary>
/// DEF-015 DB round-trip: a driver whose license is stored encrypted comes back from
/// the roster/export projections carrying ciphertext, and the production masking
/// helper renders it as last-four with no "enc:" anywhere in the serialized result.
/// Named *Postgres* so CI's unit lane skips it and the DB lane runs it.
/// </summary>
public class DriverLicenseMaskingPostgresTests
{
    private static Database CreateDatabase()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString })
            .Build();
        return new Database(config);
    }

    [Fact]
    public async Task RosterAndExportProjections_NeverReturnCiphertext_AfterMasking()
    {
        var db = CreateDatabase();
        var pii = new PiiProtectionService(new TestKeyProvider(), NullLogger<PiiProtectionService>.Instance);

        var companyId = await db.InsertAsync(
            @"INSERT INTO companies (company_code, name, industry, timezone, status)
              VALUES (@code, 'License Mask Tenant', 'Logistics', 'America/New_York', 'Active')",
            c => c.Parameters.AddWithValue("@code", $"licmask-{Guid.NewGuid():N}"));
        try
        {
            var encrypted = pii.Encrypt("MASK-ME-4321")!;
            Assert.StartsWith("enc:", encrypted);
            await db.ExecuteAsync(
                @"INSERT INTO drivers (company_id, driver_code, full_name, status, license_number, license_number_bidx)
                  VALUES (@cid, @code, 'Masked Driver', 'Available', @license, @bidx)",
                c =>
                {
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@code", $"DRV-{Guid.NewGuid():N}"[..16]);
                    c.Parameters.AddWithValue("@license", encrypted);
                    c.Parameters.AddWithValue("@bidx", (object?)pii.BlindIndex("MASK-ME-4321") ?? DBNull.Value);
                });

            // Roster projection (the handler ships d.*) — ciphertext in, masked out.
            var roster = await db.QueryAsync(
                "SELECT d.* FROM drivers d WHERE d.deleted_at IS NULL AND d.company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId));
            Assert.Single(roster);
            Assert.StartsWith("enc:", roster[0]["licenseNumber"]?.ToString());
            EndpointMappings.MaskDriverLicenseIn(roster, pii);
            var rosterJson = System.Text.Json.JsonSerializer.Serialize(roster);
            Assert.DoesNotContain("enc:", rosterJson);
            Assert.DoesNotContain("MASK-ME-4321", rosterJson);
            Assert.Equal("•••• 4321", roster[0]["licenseNumber"]);

            // Export projection — the exact column list the CSV export uses.
            var export = await db.QueryAsync(
                @"SELECT d.driver_code, d.full_name, d.phone, d.email, d.license_number, d.license_expiry,
                         d.status, d.safety_score, d.compliance_score
                  FROM drivers d WHERE d.deleted_at IS NULL AND d.company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId));
            EndpointMappings.MaskDriverLicenseIn(export, pii);
            var exportJson = System.Text.Json.JsonSerializer.Serialize(export);
            Assert.DoesNotContain("enc:", exportJson);
            Assert.Equal("•••• 4321", export[0]["licenseNumber"]);

            // Workforce projection — carries NO license material at all.
            var workforce = await db.QueryAsync(
                @"SELECT d.id, d.full_name AS name, d.driver_code AS code, '' AS licence_class
                  FROM drivers d WHERE d.company_id=@cid AND d.deleted_at IS NULL",
                c => c.Parameters.AddWithValue("@cid", companyId));
            var workforceJson = System.Text.Json.JsonSerializer.Serialize(workforce);
            Assert.DoesNotContain("enc:", workforceJson);
            Assert.DoesNotContain("4321", workforceJson);
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM drivers WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));
            await db.ExecuteAsync("DELETE FROM companies WHERE id=@c", c => c.Parameters.AddWithValue("@c", companyId));
        }
    }
}
