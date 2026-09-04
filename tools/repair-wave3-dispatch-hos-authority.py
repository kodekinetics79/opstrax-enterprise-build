from pathlib import Path

source_path = Path('backend-dotnet/Controllers/EndpointMappings.cs')
source = source_path.read_text()

# 1) AvailableDrivers: show operationally available drivers without letting legacy
# hos_records make legal-hours decisions. A fresh authoritative Stage99 clock may
# expose remaining hours; absent/stale authority remains NULL/Unavailable.
avail_start = source.index('    private static async Task<IResult> AvailableDrivers')
avail_end = source.index('    private static async Task<IResult> AvailableVehicles', avail_start)
avail = source[avail_start:avail_end]
query_start = avail.index('        var rows = await db.QueryAsync(\n')
bind_marker = '            c => { c.Parameters.AddWithValue("@cid", companyId); if (branchId is not null) c.Parameters.AddWithValue("@branchId", branchId); }, ct);'
bind_start = avail.index(bind_marker, query_start)

new_query = '''        var rows = await db.QueryAsync(
            @"SELECT d.*,
                     ROUND((COALESCE(d.readiness_score,50)+COALESCE(d.safety_score,50)+COALESCE(d.compliance_score,50))/3,1) match_readiness,
                     (SELECT COUNT(*) FROM dvir_defects dd2
                      WHERE dd2.company_id=d.company_id AND dd2.driver_id=d.id
                        AND dd2.status NOT IN ('resolved','Resolved')) open_defect_count,
                     hos.drive_time_remaining_minutes / 60.0 available_hos_hours,
                     COALESCE(hos.status,'Unavailable') hos_clock_status,
                     hos.clock_source hos_clock_source,
                     hos.source_observed_at hos_source_observed_at,
                     (SELECT COUNT(*) FROM dispatch_assignments da2
                      WHERE da2.driver_id=d.id
                        AND da2.assignment_status NOT IN ('delivered','cancelled')
                        AND da2.company_id=@cid) active_assignment_count,
                     CASE WHEN COALESCE(d.safety_score,100) < 65 THEN 1 ELSE 0 END safety_blocked,
                     CASE WHEN d.status NOT IN ('Available','Idle') THEN 1 ELSE 0 END status_blocked
              FROM drivers d
              LEFT JOIN LATERAL (
                  SELECT hc.drive_time_remaining_minutes, hc.status, hc.clock_source, hc.source_observed_at
                  FROM hos_clocks hc
                  WHERE hc.driver_id=d.id AND hc.company_id=@cid
                    AND hc.source_authority='Authoritative'
                    AND NULLIF(BTRIM(hc.clock_source),'') IS NOT NULL
                    AND hc.source_observed_at IS NOT NULL
                    AND hc.source_observed_at >= NOW() - INTERVAL '24 hours'
                  ORDER BY hc.source_observed_at DESC, hc.id DESC
                  LIMIT 1
              ) hos ON TRUE
              WHERE d.company_id=@cid AND d.deleted_at IS NULL
                AND d.status IN ('Available','Idle')
                AND COALESCE(d.safety_score,0) >= 65
                AND NOT EXISTS (SELECT 1 FROM dispatch_assignments da2
                                WHERE da2.driver_id=d.id AND da2.company_id=@cid
                                  AND da2.assignment_status NOT IN ('delivered','cancelled'))" + branchClause + @"
              ORDER BY match_readiness DESC",
'''
avail = avail[:query_start] + new_query + avail[bind_start:]
source = source[:avail_start] + avail + source[avail_end:]

# 2) Core dispatch eligibility: only a fresh Stage99-authoritative hos_clocks row
# may block/warn on legal remaining-time. No clock => explicit manual verification
# warning, not an inferred safe/illegal state.
eligibility_start = source.index('    internal static async Task<DispatchEligibilityResult> CheckDispatchEligibilityAsync')
hos_start_marker = '        // HOS check — integration-ready; uses hos_records if data exists.\n'
hos_start = source.index(hos_start_marker, eligibility_start)
hos_end = source.index('        // Safety events — critical unresolved flags.\n', hos_start)

new_hos = '''        // Legal-time decisions are fail-closed on source truth: only a fresh,
        // tenant-scoped Stage99 Authoritative clock may influence dispatch. Legacy
        // hos_records remain compatibility/demo data and are never legal authority.
        decimal? availableHosHours = null;
        bool hosWarning = false;
        Dictionary<string, object?>? hosClock = null;
        try
        {
            if (await db.ScalarLongAsync(
                    "SELECT CASE WHEN to_regclass('public.hos_clocks') IS NULL THEN 0 ELSE 1 END", ct: ct) == 1)
            {
                hosClock = await db.QuerySingleAsync(
                    @"SELECT drive_time_remaining_minutes, status, clock_source, source_observed_at
                      FROM hos_clocks
                      WHERE driver_id=@did AND company_id=@cid
                        AND source_authority='Authoritative'
                        AND NULLIF(BTRIM(clock_source),'') IS NOT NULL
                        AND source_observed_at IS NOT NULL
                        AND source_observed_at >= NOW() - INTERVAL '24 hours'
                      ORDER BY source_observed_at DESC, id DESC
                      LIMIT 1",
                    c =>
                    {
                        c.Parameters.AddWithValue("@did", driverId);
                        c.Parameters.AddWithValue("@cid", companyId);
                    }, ct);
            }
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UndefinedTable
            or PostgresErrorCodes.InsufficientPrivilege or PostgresErrorCodes.UndefinedColumn)
        {
            hosClock = null;
        }

        if (hosClock is not null)
        {
            if (hosClock["driveTimeRemainingMinutes"] is not null and not DBNull)
                availableHosHours = Convert.ToDecimal(hosClock["driveTimeRemainingMinutes"]) / 60m;

            var clockStatus = hosClock["status"]?.ToString() ?? "Unavailable";
            if (clockStatus.Equals("Violation", StringComparison.OrdinalIgnoreCase))
            {
                blocking.Add("Authoritative HOS clock reports a violation — cannot dispatch");
                hosWarning = true;
            }
            else if (availableHosHours.HasValue && availableHosHours.Value < 1m)
            {
                blocking.Add($"Driver has only {availableHosHours:N1}h authoritative remaining drive time — cannot dispatch");
                hosWarning = true;
            }
            else if (availableHosHours.HasValue && availableHosHours.Value < 3m)
            {
                warnings.Add($"Driver has {availableHosHours:N1}h authoritative remaining drive time — limited availability");
                hosWarning = true;
            }
            else if (clockStatus.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Authoritative HOS clock reports a warning status — verify before dispatch");
                hosWarning = true;
            }
        }
        else
        {
            warnings.Add("Authoritative HOS clock unavailable or stale — manual verification required before dispatch");
        }

'''
source = source[:hos_start] + new_hos + source[hos_end:]
source_path.write_text(source)

# Focused source-level contract: protects this authority boundary without requiring
# a provider account or pretending software tests are regulatory certification.
test_path = Path('backend-dotnet.Tests/DispatchHosAuthorityContractTests.cs')
test_path.write_text(r'''using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class DispatchHosAuthorityContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Source => File.ReadAllText(
        Path.Combine(Root, "backend-dotnet", "Controllers", "EndpointMappings.cs"));

    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"start marker not found: {startMarker}");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"end marker not found: {endMarker}");
        return source[start..end];
    }

    [Fact]
    public void AvailableDrivers_DoesNotUseLegacyHosRecordsAsLegalAuthority()
    {
        var slice = Between(
            Source,
            "private static async Task<IResult> AvailableDrivers",
            "private static async Task<IResult> AvailableVehicles");

        Assert.DoesNotContain("hos_records", slice, StringComparison.Ordinal);
        Assert.Contains("FROM hos_clocks hc", slice, StringComparison.Ordinal);
        Assert.Contains("hc.source_authority='Authoritative'", slice, StringComparison.Ordinal);
        Assert.Contains("hc.source_observed_at >= NOW() - INTERVAL '24 hours'", slice, StringComparison.Ordinal);
        Assert.Contains("available_hos_hours", slice, StringComparison.Ordinal);
        Assert.Contains("COALESCE(hos.status,'Unavailable')", slice, StringComparison.Ordinal);
    }

    [Fact]
    public void DispatchEligibility_UsesOnlyFreshAuthoritativeHosClock()
    {
        var slice = Between(
            Source,
            "internal static async Task<DispatchEligibilityResult> CheckDispatchEligibilityAsync",
            "// Safety events — critical unresolved flags.");

        Assert.DoesNotContain("hos_records", slice, StringComparison.Ordinal);
        Assert.DoesNotContain("IsOperableHosStatus", slice, StringComparison.Ordinal);
        Assert.Contains("FROM hos_clocks", slice, StringComparison.Ordinal);
        Assert.Contains("source_authority='Authoritative'", slice, StringComparison.Ordinal);
        Assert.Contains("source_observed_at >= NOW() - INTERVAL '24 hours'", slice, StringComparison.Ordinal);
        Assert.Contains("Authoritative HOS clock unavailable or stale", slice, StringComparison.Ordinal);
    }
}
''')
