using System.Text.RegularExpressions;

namespace Opstrax.Tests;

// Non-DB lane. Directory-vs-runner parity for tools/apply-neon-predeploy-migrations.sh
// (DEF-010 adjacent): a migration file sitting in database/migrations/ that the runner
// never applies is invisible drift — stage83_platform_settings sat unenrolled while
// production saved SMTP settings into a table that did not exist. Every *.sql file must
// be exactly one of: enrolled in the MIGRATIONS array, applied by the runner OUTSIDE
// the array (the terminal security cutover), applied out-of-band before the runner
// (CI/dev RLS preflight), or named on the documented orphan list below.
public sealed class MigrationRunnerEnrollmentParityTests
{
    [Fact]
    public void EveryMigrationFile_IsEnrolledOrExplicitlyAccountedFor()
    {
        var enrolled = RunnerArrayEntries();
        var disk = MigrationFilesOnDisk();

        // No ghost entries: everything the runner would apply must exist.
        foreach (var entry in enrolled.Where(e => !e.StartsWith("../", StringComparison.Ordinal)))
            Assert.Contains(entry, disk);

        var accounted = new HashSet<string>(enrolled);
        accounted.UnionWith(AppliedByRunnerAfterTheArray);
        accounted.UnionWith(AppliedOutOfBandBeforeTheRunner);
        accounted.UnionWith(UnenrolledOrphans);

        var unaccounted = disk.Where(f => !accounted.Contains(f)).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.True(unaccounted.Length == 0,
            "Migration files neither enrolled in the runner MIGRATIONS array nor documented "
            + "(enroll them in chronological position, or add them to the explicit skip list "
            + "in this test with justification):\n  " + string.Join("\n  ", unaccounted));

        // The categories must not overlap — a file cannot be both enrolled and skipped.
        foreach (var name in AppliedByRunnerAfterTheArray.Concat(AppliedOutOfBandBeforeTheRunner).Concat(UnenrolledOrphans))
            Assert.DoesNotContain(name, enrolled);

        // Skip-list hygiene: every documented file must still exist; an orphan that has
        // since been enrolled must be REMOVED from the list, so the list only shrinks.
        foreach (var name in AppliedByRunnerAfterTheArray.Concat(AppliedOutOfBandBeforeTheRunner).Concat(UnenrolledOrphans))
            Assert.Contains(name, disk);
    }

    [Fact]
    public void TerminalCutoverFiles_AreActuallyAppliedByTheRunnerOutsideTheArray()
    {
        var runner = RunnerText();
        foreach (var name in AppliedByRunnerAfterTheArray)
            Assert.Contains($"-f database/migrations/{name}.sql", runner, StringComparison.Ordinal);
    }

    [Fact]
    public void PreTerminalDriverProofMigration_DoesNotRequireTheSystemRoleOnAnEmptyCluster()
    {
        var migration = File.ReadAllText(Path.Combine(
            RepoRoot(), "database", "migrations",
            "2026_08_13_stage80_driver_proof_upload_binding.sql"));

        Assert.Contains("IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='opstrax_system')", migration,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE dispatch_proof_uploads TO opstrax_system;",
            RemoveDoBlocks(migration), StringComparison.Ordinal);
    }

    private static string RemoveDoBlocks(string sql) =>
        Regex.Replace(sql, @"DO\s+\$[^$]+\$.*?\$[^$]+\$\s*;", "", RegexOptions.Singleline);

    [Fact]
    public void NewlyEnrolledMigrations_SitInChronologicalOrder()
    {
        var enrolled = RunnerArrayEntries();
        int IndexOf(string name) => enrolled.IndexOf(name);

        Assert.True(IndexOf("2026_08_21_stage83_company_security_settings_runtime_contract")
                    < IndexOf("2026_08_21_stage83_platform_settings"));
        Assert.True(IndexOf("2026_08_21_stage83_platform_settings")
                    < IndexOf("2026_08_21_stage84_driver_hos_runtime_contract"));
        Assert.True(IndexOf("2026_08_21_stage85_alert_notification_delivery")
                    < IndexOf("2026_08_22_stage86_runtime_route_column_contract"));
        Assert.True(IndexOf("2026_08_22_stage86_runtime_route_column_contract")
                    < IndexOf("2026_08_22_stage87_user_role_id_backfill"));

        // The tail must stay chronological. This used to be `Assert.Equal(count-1,
        // IndexOf(stage87))`, which pinned stage87 as the LAST entry and therefore had
        // to be edited by hand for every subsequent migration — a test that has to be
        // rewritten to stay green is not measuring anything. The invariant that
        // actually matters is that nothing older is appended after it. (One deliberate
        // exception exists earlier in the array — stage12a telemetry live state is
        // replayed after stage51 — which is why this is a tail rule, not a global sort.)
        var afterStage87 = enrolled.Skip(IndexOf("2026_08_22_stage87_user_role_id_backfill") + 1);
        foreach (var entry in afterStage87)
        {
            var date = Regex.Match(entry, @"^(\d{4}_\d{2}_\d{2})_");
            Assert.True(date.Success,
                $"Undated migration '{entry}' appended after stage87 — new enrollments must be date-prefixed.");
            Assert.True(string.CompareOrdinal(date.Groups[1].Value, "2026_08_22") >= 0,
                $"'{entry}' is enrolled after stage87 but is dated earlier — enrol it in chronological position.");
        }
    }

    // ── The rule that replaces the false rationale ───────────────────────────────
    // The skip list below used to justify itself with the sentence "these are the July
    // feature-wave files whose objects were later re-established by the consolidated
    // owner waves the runner DOES apply". That was measurably FALSE. Verified at
    // RETEST-20260821-1035-R1 against the migration-pure database `staging_upgrade`
    // (exactly the 69 enrolled migrations, no Dev boot): 33 tables created ONLY by an
    // orphan file are absent from it, and every one of them is read or written by
    // controller/service SQL. GET /api/settings/api-keys returns 500/42P01 for a
    // legitimately gated internal user because tenant_api_keys is one of them.
    //
    // Prose cannot be re-checked by CI, so it is replaced by a mechanical rule:
    //
    //   An unenrolled orphan may not create a table that (a) the ENROLLED corpus does
    //   not also create and (b) controller or service SQL references.
    //
    // An orphan whose objects genuinely were re-established elsewhere passes this
    // automatically — that is what "re-established" means when it is true.
    [Fact]
    public void UnenrolledOrphans_CreateNothingTheRunningCodeDependsOn()
    {
        var enrolled = RuntimeSchemaMigrationParityTests.EnrolledTables();
        var consumed = TablesReferencedByRunningCode();
        Assert.True(consumed.Count > 200, $"controller/service SQL table extraction collapsed: {consumed.Count} tables");

        var violations = new List<string>();
        var orphanTables = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var orphan in UnenrolledOrphans.OrderBy(o => o, StringComparer.Ordinal))
        {
            var path = Path.Combine(RepoRoot(), "database", "migrations", orphan + ".sql");
            Assert.True(File.Exists(path), $"orphan file missing on disk: {orphan}");
            var unmet = RuntimeSchemaMigrationParityTests
                .CreateTableNames(File.ReadAllText(path))
                .Where(t => !enrolled.Contains(t))
                .Where(consumed.Contains)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();
            if (unmet.Length == 0) continue;
            foreach (var t in unmet) orphanTables.Add(t);
            violations.Add($"{orphan}\n      -> {string.Join(", ", unmet)}");
        }

        Assert.True(violations.Count == 0,
            $"{violations.Count} unenrolled orphan migrations create {orphanTables.Count} tables that exist in NO "
            + "enrolled migration and that controller/service SQL queries. Each is a live 42P01 in staging and "
            + "production. Enrol the file in chronological position in "
            + "tools/apply-neon-predeploy-migrations.sh (the hygiene assertion above then forces its removal from "
            + "this list), or fold its objects into a new owner migration — do NOT re-document it:\n  "
            + string.Join("\n  ", violations));
    }

    // Tables the RUNNING code reads or writes: every SQL string literal in
    // backend-dotnet/Controllers/*.cs and backend-dotnet/Services/*.cs, minus the
    // *SchemaService.cs files (their DDL is the declaration under suspicion, not
    // evidence that anything consumes the table).
    private static HashSet<string> TablesReferencedByRunningCode()
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in new[] { "Controllers", "Services" })
        {
            var path = Path.Combine(RepoRoot(), "backend-dotnet", dir);
            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.TopDirectoryOnly)
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                if (Path.GetFileName(file).EndsWith("SchemaService.cs", StringComparison.Ordinal)) continue;
                foreach (var literal in RuntimeSchemaMigrationParityTests.SqlLiteralsIn(File.ReadAllText(file)))
                    foreach (Match m in Regex.Matches(literal,
                                 @"\b(?:FROM|JOIN|INTO|UPDATE)\s+(?:ONLY\s+)?(?:public\.)?([a-z_][a-z0-9_]*)",
                                 RegexOptions.IgnoreCase))
                        tables.Add(m.Groups[1].Value.ToLowerInvariant());
            }
        }
        return tables;
    }

    // ── parsing ──────────────────────────────────────────────────────────────

    private static string RunnerText() => File.ReadAllText(Path.Combine(RepoRoot(), "tools", "apply-neon-predeploy-migrations.sh"));

    private static List<string> RunnerArrayEntries()
    {
        var match = Regex.Match(RunnerText(), @"MIGRATIONS=\((.*?)\n\)", RegexOptions.Singleline);
        Assert.True(match.Success, "MIGRATIONS array not found in apply-neon-predeploy-migrations.sh — update this test's parser.");
        var entries = match.Groups[1].Value
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
        Assert.True(entries.Count >= 60, $"MIGRATIONS array parse collapsed: {entries.Count} entries");
        return entries;
    }

    private static HashSet<string> MigrationFilesOnDisk()
    {
        var dir = Path.Combine(RepoRoot(), "database", "migrations");
        var names = Directory.EnumerateFiles(dir, "*.sql", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .Concat(Directory.EnumerateFiles(Path.Combine(dir, "telematics"), "*.sql", SearchOption.TopDirectoryOnly)
                .Select(f => "telematics/" + Path.GetFileNameWithoutExtension(f)!));
        return new HashSet<string>(names, StringComparer.Ordinal);
    }

    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    // ── documented skip lists ────────────────────────────────────────────────

    // Applied by the runner itself AFTER the MIGRATIONS array: the terminal security
    // cutover. Stage58 revokes stale privileges and swaps RLS policies atomically and
    // must run last; stage59 (data-protection key ring) and stage76 (telemetry ACL
    // terminal hardening) are sequenced with it.
    private static readonly string[] AppliedByRunnerAfterTheArray =
    [
        "2026_07_31_stage58_nonforgeable_tenant_ticket",
        "2026_07_31_stage59_data_protection_key_ring",
        "2026_08_11_stage76_telematics_security_hardening",
    ];

    // Applied out-of-band BEFORE the runner: the CI integration job (and a dev box)
    // applies base RLS enablement first (ci.yml "Apply base schema + production owner
    // migrations"); the runner then hard-requires the opstrax_app role stage20 creates.
    // stage20 itself IS in the array (idempotent re-run).
    private static readonly string[] AppliedOutOfBandBeforeTheRunner =
    [
        "2026_06_30_stage19_row_level_security",
        "2026_07_01_stage22_rls_reconcile_coverage",
    ];

    // Enrolled NOWHERE today: these files are in database/migrations/ but no runner, CI
    // step, or Dockerfile-consumer applies them to a protected database.
    //
    // ⚠ The rationale this list used to carry — "whose objects were later re-established
    // by the consolidated owner waves the runner DOES apply" — was FALSE, and is now
    // enforced against instead of asserted in prose: see
    // UnenrolledOrphans_CreateNothingTheRunningCodeDependsOn. Ten of the twenty-five
    // create tables that no enrolled migration creates and that the running code
    // queries. Being on THIS list is therefore not an approval; it means the file is
    // applied by nothing, and for those ten it means a live 42P01.
    //
    // Fifteen entries are genuinely inert: they are ALTER-only files, or their tables
    // (password_reset_tokens, feature_flags, telemetry gateway replay) are also created
    // by an enrolled wave. Those pass the assertion without needing to be excused.
    //
    // Enrolling a file is still a per-file review decision — several recreate pre-Stage58
    // policy surfaces the terminal cutover superseded, which is why the runner already
    // refuses to replay stage53 — but "needs review" is not a reason to keep shipping a
    // 42P01. Enrol in chronological position and the hygiene assertion above forces the
    // entry's removal from this list.
    private static readonly string[] UnenrolledOrphans =
    [
        "2026_07_05_stage27_iot_credential_quarantine",
        "2026_07_08_stage28_integration_connector_health",
        "2026_07_09_stage29_telemetry_ingest_columns",
        "2026_07_09_stage31_polygon_geofences",
        "2026_07_11_password_reset_tokens",
        "2026_07_11_stage33_tenant_api_keys_webhooks",
        "2026_07_11_users_roles_governance",
        "2026_07_13_customer_entity_integrity",
        "2026_07_13_users_email_per_tenant",
        "2026_07_14_feature_flags",
        "2026_07_14_stage33_gps_gateway_replay",
        "2026_07_15_stage35_job_charges_rating_seam",
        "2026_07_15_stage37_settlement_ap",
        "2026_07_16_stage38_tax_engine",
        "2026_07_16_stage39_billing_consolidation",
        "2026_07_16_stage41_fin_config_envelope",
        "2026_07_21_stage43_tenant_mfa",
        "2026_07_21_stage44_maintenance_pm_baseline",
        "2026_07_22_stage48_driver_detention_pay",
        "2026_08_18_stage81_platform_billing_itemization",
    ];
}
