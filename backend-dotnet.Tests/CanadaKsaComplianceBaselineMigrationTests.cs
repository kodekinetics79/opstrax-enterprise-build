using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class CanadaKsaComplianceBaselineMigrationTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Migration => File.ReadAllText(Path.Combine(
        RepoRoot, "database", "migrations", "2026_09_03_stage101_canada_ksa_compliance_baseline.sql"));

    [Fact]
    public void Stage101_CorrectsSaudiAuthorityAndNormalDailyLimit()
    {
        var sql = Migration;

        Assert.Contains("SET hos_ruleset = 'TGA Goods Transport HOS'", sql, StringComparison.Ordinal);
        Assert.Contains("authority='Transport General Authority (TGA)'", sql, StringComparison.Ordinal);
        Assert.Contains("max_driving_hours=9", sql, StringComparison.Ordinal);
        Assert.Contains("rule_code='SA-TGA-HOS-9H-DRIVE'", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE rule_code='SA-HOS-10H'", sql, StringComparison.Ordinal);
        Assert.Contains("obsolete SA-HOS-10H rule remains active", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage101_ModelsSaudiWeeklyTwoWeekBreakAndRestBoundaries()
    {
        var sql = Migration;

        Assert.Contains("SA-TGA-HOS-10H-EXT-2X", sql, StringComparison.Ordinal);
        Assert.Contains("SA-TGA-HOS-56H-7D", sql, StringComparison.Ordinal);
        Assert.Contains("SA-TGA-HOS-90H-14D", sql, StringComparison.Ordinal);
        Assert.Contains("SA-TGA-HOS-BREAK-4_5H", sql, StringComparison.Ordinal);
        Assert.Contains("SA-TGA-HOS-DAILY-REST-11H", sql, StringComparison.Ordinal);
        Assert.Contains("SA-TGA-HOS-WEEKLY-REST-48H", sql, StringComparison.Ordinal);
        Assert.Contains("SA-TGA-HOS-MAX-6D", sql, StringComparison.Ordinal);
        Assert.Contains("SA-TGA-TRACKING-PROVIDER", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage101_ExpandsCanadaSouthOf60BeyondSingleThirteenHourRule()
    {
        var sql = Migration;

        Assert.Contains("Canada Federal HOS - South of 60N", sql, StringComparison.Ordinal);
        Assert.Contains("rest_requirement_hours = 10", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-13H-DRIVE", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-14H-DUTY", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-16H-ELAPSED", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-10H-OFFDUTY", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-24H-OFF-14D", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-C1-70H-7D", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-C2-120H-14D", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-C2-70H-24H-OFF", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-C1-RESET-36H", sql, StringComparison.Ordinal);
        Assert.Contains("CA-S60-HOS-C2-RESET-72H", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage101_SeparatesCanadaNorthOf60ProfileAndCycles()
    {
        var sql = Migration;

        Assert.Contains("Canada Federal HOS - North of 60N", sql, StringComparison.Ordinal);
        Assert.Contains("CA-N60-HOS-15H-DRIVE", sql, StringComparison.Ordinal);
        Assert.Contains("CA-N60-HOS-18H-DUTY", sql, StringComparison.Ordinal);
        Assert.Contains("CA-N60-HOS-20H-ELAPSED", sql, StringComparison.Ordinal);
        Assert.Contains("CA-N60-HOS-C1-80H-7D", sql, StringComparison.Ordinal);
        Assert.Contains("CA-N60-HOS-C2-120H-14D", sql, StringComparison.Ordinal);
        Assert.Contains("CA-N60-HOS-C2-80H-24H-OFF", sql, StringComparison.Ordinal);
        Assert.Contains("CA-N60-HOS-C1-RESET-36H", sql, StringComparison.Ordinal);
        Assert.Contains("CA-N60-HOS-C2-RESET-72H", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage101_RemovesGenericCanadianCarrierRegistrationSemantics()
    {
        var sql = Migration;

        Assert.Contains("rule_code='CA-CARRIER-SAFETY-FITNESS'", sql, StringComparison.Ordinal);
        Assert.Contains("province/territory specific", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE rule_code='TC-NSC-CARRIER'", sql, StringComparison.Ordinal);
        Assert.Contains("obsolete generic TC-NSC-CARRIER rule remains active", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Stage101_FailsClosedAndDoesNotClaimCertification()
    {
        var sql = Migration;

        Assert.Contains("does NOT certify OpsTrax", sql, StringComparison.Ordinal);
        Assert.Contains("exact currently certified hardware/software boundary", sql, StringComparison.Ordinal);
        Assert.Contains("RAISE EXCEPTION 'Stage101 failed", sql, StringComparison.Ordinal);
        Assert.Contains("missing_count", sql, StringComparison.Ordinal);
        Assert.Contains("BEGIN;", sql, StringComparison.Ordinal);
        Assert.Contains("COMMIT;", sql, StringComparison.Ordinal);
    }
}
