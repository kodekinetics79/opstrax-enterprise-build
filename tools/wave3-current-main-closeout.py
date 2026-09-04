from pathlib import Path


def replace_once(text: str, old: str, new: str, label: str) -> str:
    if old not in text:
        if new in text:
            return text
        raise RuntimeError(f"{label}: expected source block not found")
    return text.replace(old, new, 1)

# -----------------------------------------------------------------------------
# 1) Dispatch and available-driver legal-time authority boundary
# -----------------------------------------------------------------------------
endpoint_path = Path('backend-dotnet/Controllers/EndpointMappings.cs')
source = endpoint_path.read_text()

avail_start = source.index('    private static async Task<IResult> AvailableDrivers')
avail_end = source.index('    private static async Task<IResult> AvailableVehicles', avail_start)
avail = source[avail_start:avail_end]
if "hc.source_authority='Authoritative'" not in avail:
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

eligibility_start = source.index('    internal static async Task<DispatchEligibilityResult> CheckDispatchEligibilityAsync')
eligibility_end = source.index('        // Safety events — critical unresolved flags.\n', eligibility_start)
eligibility_slice = source[eligibility_start:eligibility_end]
if "Authoritative HOS clock unavailable or stale" not in eligibility_slice:
    hos_start_marker = '        // HOS check — integration-ready; uses hos_records if data exists.\n'
    hos_start = source.index(hos_start_marker, eligibility_start)
    hos_end = eligibility_end
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
endpoint_path.write_text(source)

# -----------------------------------------------------------------------------
# 2) Runtime schema: no fabricated clocks; preserve source identity/authority
# -----------------------------------------------------------------------------
batch_path = Path('backend-dotnet/Services/Batch6SchemaService.cs')
batch = batch_path.read_text()
column_anchor = '        new("hos_clocks",               "branch_id",             "BIGINT NULL"),\n'
column_add = column_anchor + '''        new("hos_clocks",               "clock_source",          "VARCHAR(80) NULL"),
        new("hos_clocks",               "source_event_id",       "VARCHAR(160) NULL"),
        new("hos_clocks",               "source_observed_at",    "TIMESTAMPTZ NULL"),
        new("hos_clocks",               "source_authority",      "VARCHAR(32) NOT NULL DEFAULT 'LegacyUnverified'"),
        new("hos_clocks",               "source_quality",        "VARCHAR(32) NULL"),
'''
if 'new("hos_clocks",               "clock_source"' not in batch:
    batch = replace_once(batch, column_anchor, column_add, 'Batch6 source columns')

old_clock = '''            drive_time_remaining_minutes INT NOT NULL DEFAULT 660,
            shift_time_remaining_minutes INT NOT NULL DEFAULT 840,
            cycle_time_remaining_minutes INT NOT NULL DEFAULT 4200,
            break_needed_at TIMESTAMPTZ NULL,
            reset_at TIMESTAMPTZ NULL,
            status VARCHAR(80) NOT NULL DEFAULT 'OK',
            hos_warning VARCHAR(200) NULL,
            updated_at TIMESTAMPTZ NULL
'''
new_clock = '''            drive_time_remaining_minutes INT NULL,
            shift_time_remaining_minutes INT NULL,
            cycle_time_remaining_minutes INT NULL,
            break_needed_at TIMESTAMPTZ NULL,
            reset_at TIMESTAMPTZ NULL,
            status VARCHAR(80) NOT NULL DEFAULT 'Unavailable',
            hos_warning VARCHAR(200) NULL,
            clock_source VARCHAR(80) NULL,
            source_event_id VARCHAR(160) NULL,
            source_observed_at TIMESTAMPTZ NULL,
            source_authority VARCHAR(32) NOT NULL DEFAULT 'LegacyUnverified',
            source_quality VARCHAR(32) NULL,
            updated_at TIMESTAMPTZ NULL
'''
batch = replace_once(batch, old_clock, new_clock, 'Batch6 hos_clocks schema')

old_seed = '''          (1,1,'US',1,'70hr/8day',480,620,3900,'OK',NULL),
          (2,2,'US',1,'70hr/8day',55,90,240,'Warning','Approaching drive limit'),
          (3,3,'US',1,'70hr/8day',660,840,4200,'OK',NULL),
          (4,4,'US',1,'70hr/8day',0,120,900,'Violation','11-hour driving limit exceeded'),
          (5,5,'US',1,'70hr/8day',300,480,2400,'OK',NULL),
          (6,6,'CA',3,'13hr/day',540,660,3600,'OK',NULL),
          (7,7,'CA',3,'13hr/day',90,150,600,'Warning','Approaching duty limit'),
          (8,8,'SA',4,'10hr/day',420,540,2800,'OK',NULL),
          (9,9,'AE',5,'10hr/day',360,480,2600,'OK',NULL),
          (10,10,'PK',6,'10hr/day',300,420,2200,'OK',NULL)
'''
new_seed = '''          (1,1,'US',1,'70hr/8day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'),
          (2,2,'US',1,'70hr/8day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'),
          (3,3,'US',1,'70hr/8day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'),
          (4,4,'US',1,'70hr/8day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'),
          (5,5,'US',1,'70hr/8day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'),
          (6,6,'CA',3,'13hr/day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'),
          (7,7,'CA',3,'13hr/day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'),
          (8,8,'SA',4,'10hr/day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'),
          (9,9,'AE',5,'10hr/day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'),
          (10,10,'PK',6,'10hr/day',NULL,NULL,NULL,'Unavailable','Authoritative HOS source required')
'''
batch = replace_once(batch, old_seed, new_seed, 'Batch6 HOS seed truth')
batch_path.write_text(batch)

# -----------------------------------------------------------------------------
# 3) Operational alerts: legacy/demo HOS records cannot emit legal-time alerts
# -----------------------------------------------------------------------------
alert_path = Path('backend-dotnet/Services/OperationalAlertDetectionService.cs')
alerts = alert_path.read_text()
alerts = alerts.replace(
    '            await SweepAsync(db, "hos_violation", HosRecordsSql, ct);\n            await SweepAsync(db, "hos_violation(clocks)", HosClocksSql, ct);',
    '            await SweepAsync(db, "hos_violation(authoritative_clocks)", HosClocksSql, ct);')
start = alerts.find('    // Latest hos_records row per driver')
end = alerts.find('    // Companion source: hos_clocks', start)
if start >= 0 and end > start:
    alerts = alerts[:start] + '''    // HOS alerting is a compliance/safety claim, not a convenience signal. The legacy
    // hos_records table can contain demo/manual values and has no certified-source
    // provenance contract, so it is deliberately NOT consumed here. Stage99 makes
    // hos_clocks fail closed and permits actionable values only for rows explicitly
    // marked Authoritative with persisted source identity and observation time.
''' + alerts[end + len('    // Companion source: hos_clocks carries an explicit status (\'Violation\'|\'Warning\'|\'OK\')\n    // and remaining drive minutes. Only clocks touched in the last 24h count as live.\n'):]
old_where = '''          AND c.updated_at > NOW() - INTERVAL '24 hours'
          AND (c.status = 'Violation' OR c.drive_time_remaining_minutes <= 0)
'''
new_where = '''          AND c.source_authority = 'Authoritative'
          AND c.clock_source IS NOT NULL AND BTRIM(c.clock_source) <> ''
          AND c.source_observed_at IS NOT NULL
          AND c.source_observed_at > NOW() - INTERVAL '24 hours'
          AND (c.status = 'Violation' OR c.drive_time_remaining_minutes <= 0)
'''
alerts = replace_once(alerts, old_where, new_where, 'authoritative HOS alert filter')
alert_path.write_text(alerts)

# -----------------------------------------------------------------------------
# 4) Stage99 migration and migration chain enrollment
# -----------------------------------------------------------------------------
migration_path = Path('database/migrations/2026_09_03_stage99_hos_clock_source_truth.sql')
migration_path.write_text('''-- Stage 99 — Wave 3 G3A HOS clock source truth
BEGIN;

ALTER TABLE hos_clocks
  ADD COLUMN IF NOT EXISTS clock_source VARCHAR(80) NULL,
  ADD COLUMN IF NOT EXISTS source_event_id VARCHAR(160) NULL,
  ADD COLUMN IF NOT EXISTS source_observed_at TIMESTAMPTZ NULL,
  ADD COLUMN IF NOT EXISTS source_authority VARCHAR(32) NOT NULL DEFAULT 'LegacyUnverified',
  ADD COLUMN IF NOT EXISTS source_quality VARCHAR(32) NULL;

ALTER TABLE hos_clocks
  ALTER COLUMN drive_time_remaining_minutes DROP NOT NULL,
  ALTER COLUMN drive_time_remaining_minutes DROP DEFAULT,
  ALTER COLUMN shift_time_remaining_minutes DROP NOT NULL,
  ALTER COLUMN shift_time_remaining_minutes DROP DEFAULT,
  ALTER COLUMN cycle_time_remaining_minutes DROP NOT NULL,
  ALTER COLUMN cycle_time_remaining_minutes DROP DEFAULT,
  ALTER COLUMN status SET DEFAULT 'Unavailable';

UPDATE hos_clocks
SET drive_time_remaining_minutes = NULL,
    shift_time_remaining_minutes = NULL,
    cycle_time_remaining_minutes = NULL,
    break_needed_at = NULL,
    reset_at = NULL,
    status = 'Unavailable',
    hos_warning = 'Authoritative ELD/HOS source not connected',
    clock_source = NULL,
    source_event_id = NULL,
    source_observed_at = NULL,
    source_authority = 'LegacyUnverified',
    source_quality = NULL,
    updated_at = NOW();

CREATE OR REPLACE FUNCTION stage99_enforce_hos_clock_source_truth()
RETURNS TRIGGER LANGUAGE plpgsql AS $fn$
BEGIN
  IF COALESCE(NEW.source_authority, 'LegacyUnverified') <> 'Authoritative' THEN
    NEW.source_authority := COALESCE(NULLIF(BTRIM(NEW.source_authority), ''), 'LegacyUnverified');
    IF NEW.source_authority NOT IN ('LegacyUnverified','ProviderPending') THEN
      NEW.source_authority := 'LegacyUnverified';
    END IF;
    NEW.drive_time_remaining_minutes := NULL;
    NEW.shift_time_remaining_minutes := NULL;
    NEW.cycle_time_remaining_minutes := NULL;
    NEW.break_needed_at := NULL;
    NEW.reset_at := NULL;
    NEW.status := 'Unavailable';
    NEW.hos_warning := 'Authoritative ELD/HOS source not connected';
    NEW.clock_source := NULL;
    NEW.source_event_id := NULL;
    NEW.source_observed_at := NULL;
    NEW.source_quality := NULL;
  END IF;
  RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS trg_stage99_enforce_hos_clock_source_truth ON hos_clocks;
CREATE TRIGGER trg_stage99_enforce_hos_clock_source_truth
BEFORE INSERT OR UPDATE ON hos_clocks
FOR EACH ROW EXECUTE FUNCTION stage99_enforce_hos_clock_source_truth();

DO $stage99$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conrelid = 'hos_clocks'::regclass
      AND conname = 'ck_hos_clocks_source_authority'
  ) THEN
    ALTER TABLE hos_clocks
      ADD CONSTRAINT ck_hos_clocks_source_authority
      CHECK (
        (source_authority = 'Authoritative'
         AND clock_source IS NOT NULL AND BTRIM(clock_source) <> ''
         AND source_observed_at IS NOT NULL
         AND drive_time_remaining_minutes IS NOT NULL
         AND shift_time_remaining_minutes IS NOT NULL
         AND cycle_time_remaining_minutes IS NOT NULL
         AND drive_time_remaining_minutes >= 0
         AND shift_time_remaining_minutes >= 0
         AND cycle_time_remaining_minutes >= 0
         AND status IN ('OK','Warning','Violation'))
        OR
        (source_authority IN ('LegacyUnverified','ProviderPending')
         AND drive_time_remaining_minutes IS NULL
         AND shift_time_remaining_minutes IS NULL
         AND cycle_time_remaining_minutes IS NULL
         AND status = 'Unavailable')
      ) NOT VALID;
  END IF;
END
$stage99$;

ALTER TABLE hos_clocks VALIDATE CONSTRAINT ck_hos_clocks_source_authority;
CREATE INDEX IF NOT EXISTS idx_hos_clocks_company_branch_authority
  ON hos_clocks(company_id, branch_id, source_authority, source_observed_at DESC);
COMMIT;
''')

predeploy_path = Path('tools/apply-neon-predeploy-migrations.sh')
predeploy = predeploy_path.read_text()
if '2026_09_03_stage99_hos_clock_source_truth' not in predeploy:
    marker = '  2026_09_02_stage98_optional_gps_measurements\n'
    predeploy = replace_once(predeploy, marker, marker + '  # HOS legal-time values fail closed unless an authoritative source is persisted.\n  2026_09_03_stage99_hos_clock_source_truth\n', 'Stage99 migration enrollment')
predeploy_path.write_text(predeploy)

# -----------------------------------------------------------------------------
# 5) Regression tests
# -----------------------------------------------------------------------------
Path('backend-dotnet.Tests/DispatchHosAuthorityContractTests.cs').write_text(r'''using System;
using System.IO;
using Xunit;
namespace Opstrax.Tests;
public sealed class DispatchHosAuthorityContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Source => File.ReadAllText(Path.Combine(Root, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    private static string Between(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal); Assert.True(start >= 0);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal); Assert.True(end > start);
        return source[start..end];
    }
    [Fact] public void AvailableDrivers_UsesOnlyFreshAuthoritativeClocks()
    {
        var s = Between(Source, "private static async Task<IResult> AvailableDrivers", "private static async Task<IResult> AvailableVehicles");
        Assert.DoesNotContain("FROM hos_records", s, StringComparison.Ordinal);
        Assert.Contains("FROM hos_clocks hc", s, StringComparison.Ordinal);
        Assert.Contains("hc.source_authority='Authoritative'", s, StringComparison.Ordinal);
        Assert.Contains("hc.source_observed_at >= NOW() - INTERVAL '24 hours'", s, StringComparison.Ordinal);
    }
    [Fact] public void DispatchEligibility_FailsClosedWithoutFreshAuthority()
    {
        var s = Between(Source, "internal static async Task<DispatchEligibilityResult> CheckDispatchEligibilityAsync", "// Safety events — critical unresolved flags.");
        Assert.DoesNotContain("FROM hos_records", s, StringComparison.Ordinal);
        Assert.Contains("source_authority='Authoritative'", s, StringComparison.Ordinal);
        Assert.Contains("Authoritative HOS clock unavailable or stale", s, StringComparison.Ordinal);
    }
}
''')

Path('backend-dotnet.Tests/HosClockSourceTruthMigrationTests.cs').write_text(r'''using System;
using System.IO;
using Xunit;
namespace Opstrax.Tests;
public sealed class HosClockSourceTruthMigrationTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Sql => File.ReadAllText(Path.Combine(Root,"database","migrations","2026_09_03_stage99_hos_clock_source_truth.sql"));
    [Fact] public void Stage99_RemovesFabricatedDefaultsAndAddsAuthority()
    {
        Assert.Contains("source_authority VARCHAR(32)", Sql, StringComparison.Ordinal);
        Assert.Contains("DROP DEFAULT", Sql, StringComparison.Ordinal);
        Assert.Contains("drive_time_remaining_minutes = NULL", Sql, StringComparison.Ordinal);
        Assert.Contains("source_authority = 'LegacyUnverified'", Sql, StringComparison.Ordinal);
        Assert.Contains("BEFORE INSERT OR UPDATE ON hos_clocks", Sql, StringComparison.Ordinal);
        Assert.Contains("VALIDATE CONSTRAINT ck_hos_clocks_source_authority", Sql, StringComparison.Ordinal);
    }
    [Fact] public void CurrentHosUiRendersMissingClockAsUnavailable()
    {
        var page = File.ReadAllText(Path.Combine(Root,"frontend","src","pages","HosEldPage.tsx"));
        Assert.Contains("if (value == null) return", page, StringComparison.Ordinal);
        Assert.Contains("Clock value unavailable", page, StringComparison.Ordinal);
    }
}
''')

Path('backend-dotnet.Tests/HosRuntimeTruthContractTests.cs').write_text(r'''using System;
using System.IO;
using Xunit;
namespace Opstrax.Tests;
public sealed class HosRuntimeTruthContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    [Fact] public void RuntimeSchema_DoesNotRecreateLegalTimeDefaults()
    {
        var s = File.ReadAllText(Path.Combine(Root,"backend-dotnet","Services","Batch6SchemaService.cs"));
        Assert.DoesNotContain("drive_time_remaining_minutes INT NOT NULL DEFAULT 660", s, StringComparison.Ordinal);
        Assert.Contains("drive_time_remaining_minutes INT NULL", s, StringComparison.Ordinal);
        Assert.Contains("status VARCHAR(80) NOT NULL DEFAULT 'Unavailable'", s, StringComparison.Ordinal);
        Assert.Contains("source_authority", s, StringComparison.Ordinal);
    }
}
''')

Path('backend-dotnet.Tests/HosOperationalAlertSourceTruthTests.cs').write_text(r'''using System;
using System.IO;
using Xunit;
namespace Opstrax.Tests;
public sealed class HosOperationalAlertSourceTruthTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    [Fact] public void HosAlerts_RequireFreshAuthoritativeClock()
    {
        var s = File.ReadAllText(Path.Combine(Root,"backend-dotnet","Services","OperationalAlertDetectionService.cs"));
        Assert.DoesNotContain("HosRecordsSql", s, StringComparison.Ordinal);
        Assert.Contains("c.source_authority = 'Authoritative'", s, StringComparison.Ordinal);
        Assert.Contains("c.source_observed_at > NOW() - INTERVAL '24 hours'", s, StringComparison.Ordinal);
    }
}
''')

# -----------------------------------------------------------------------------
# 6) Evidence ledger — controllable engineering versus external evidence boundary
# -----------------------------------------------------------------------------
ledger = Path('docs/commercialization/wave3/G3A_CURRENT_MAIN_CLOSEOUT.md')
ledger.parent.mkdir(parents=True, exist_ok=True)
ledger.write_text('''# G3A Current-Main Closeout\n\nThis branch closes the controllable HOS source-truth P1 on current main.\n\nSoftware boundary after this change:\n- no seeded/default remaining-time value is treated as legal authority;\n- dispatch and available-driver legal-time data require a fresh tenant-scoped Authoritative `hos_clocks` source;\n- operational HOS violation alerts require the same persisted authority/provenance boundary;\n- unknown/unverified clocks are null/Unavailable and fail closed;\n- Stage99 is enrolled in the canonical protected migration chain;\n- daily HOS certification and ELD malfunction/recovery workflows already present in current main remain intact.\n\nThis is **ENGINEERING COMPLETE / EXTERNAL EVIDENCE HOLD**, not regulated ELD/HOS certification. Final HOS promotion still requires the selected certified ELD/provider/device/application boundary, authentic source events, jurisdiction-specific acceptance, visible Chrome evidence and independent regulatory/Security/SDET/Fleet Product acceptance under #116/#128.\n''')

print('Wave 3 current-main closeout patch applied.')
