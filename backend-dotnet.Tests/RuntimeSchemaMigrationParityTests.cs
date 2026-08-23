using System.Text;
using System.Text.RegularExpressions;

namespace Opstrax.Tests;

// Non-DB lane. Guards against the DEF-016/017/018/019/020 failure class: a table or
// column declared only in a runtime *SchemaService with NO enrolled migration creating
// it. Protected environments skip the runtime schema services entirely (Program.cs
// ShouldRunSchemaInitAsync bails out under the restricted role + FORCE RLS), so such an
// object exists on a dev box and NEVER in staging/production — endpoints 500 with 42703
// (column) or 42P01 (relation) while readiness stays green.
//
// ── What this test was, and why it certified a falsehood (RETEST-20260821-1035-R1) ────
// The previous revision had three proven holes, all of which produced FALSE PASSES:
//
//   1. It read exactly 9 of the 48 backend-dotnet/Services/*SchemaService.cs files
//      (Batch1-7 + Driver + Security) and only ALTER-style column declarations. Every
//      runtime-only TABLE (export_requests, sso_connections, access_reviews,
//      alert_rules, messaging_conversations, safety_coaching_tasks, …) was invisible.
//   2. It matched a column name TABLE-UNQUALIFIED (`\bcolumn\b`) against one flat text
//      corpus, so a pair passed if the NAME existed anywhere on ANY table in ANY file.
//      Proven false positive: routes.cost_estimate was reported covered while being
//      absent from a migration-pure database AND selected by RoutesSummary.
//   3. The corpus was "every .sql file on disk", including the 25 files enrolled in no
//      runner at all (see MigrationRunnerEnrollmentParityTests). A file nothing applies
//      cannot create a column in a protected environment.
//
// The corpus below is therefore the ENROLLED chain only, assembled exactly the way
// ci.yml's dotnet-integration-tests job and tools/apply-neon-predeploy-migrations.sh
// assemble it. Validated at RETEST-20260821-1035-R1 against the migration-pure database
// `staging_upgrade`: of the (table,column) pairs this parser reports as created by the
// chain, 0 were absent from that database, and of the pairs it reports as MISSING, 0
// were present. The 81 columns the parser does not model are the runtime-generated
// canonical_telemetry_events_YYYY_MM partitions, which no schema service declares.
public sealed class RuntimeSchemaMigrationParityTests
{
    [Fact]
    public void EverySchemaServiceColumn_AppearsInEnrolledMigrationSql_OrIsExplicitlyAllowlisted()
    {
        var runtime = SchemaServiceDeclarations();
        var chain = EnrolledMigrationIndex();

        // Extraction sanity: a refactor of any declaration shape must not silently
        // reduce this test's coverage. These floors are ~10% below the measured values.
        Assert.True(runtime.Pairs.Count >= 3_800, $"schema-service column extraction collapsed: {runtime.Pairs.Count} pairs");
        Assert.True(runtime.Tables.Count >= 240, $"schema-service table extraction collapsed: {runtime.Tables.Count} tables");
        Assert.True(chain.Pairs.Count >= 3_500, $"enrolled-migration column index collapsed: {chain.Pairs.Count} pairs");
        Assert.True(chain.Tables.Count >= 260, $"enrolled-migration table index collapsed: {chain.Tables.Count} tables");
        Assert.Contains(("routes", "sla_risk"), runtime.Pairs);                 // Batch2 ColumnDefinition[]
        Assert.Contains(("drivers", "user_id"), runtime.Pairs);                 // DriverSchemaService tuple array
        Assert.Contains(("users", "failed_login_attempts"), runtime.Pairs);     // SecuritySchemaService inline ALTER
        Assert.Contains(("safety_coaching_tasks", "coaching_type"), runtime.Pairs); // SafetySchemaService CREATE TABLE
        Assert.Contains("export_requests", runtime.Tables);                     // runtime-only CREATE TABLE

        // Columns on tables the enrolled chain DOES create. A runtime-only TABLE is
        // reported by EverySchemaServiceTable_IsCreatedByTheEnrolledMigrationChain
        // instead — listing each of its columns here would be the same defect counted
        // fifteen times.
        var missing = runtime.Pairs
            .Where(p => chain.Tables.Contains(p.Table))
            .Where(p => !chain.Pairs.Contains(p))
            .Where(p => !KnownRuntimeOnlyColumns.Contains(p))
            .OrderBy(p => p.Table, StringComparer.Ordinal).ThenBy(p => p.Column, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0,
            $"{missing.Length} schema-service columns have NO enrolled migration creating them on that table "
            + "(the DEF-016 class — every one of these is a 42703 in staging/production the moment a request "
            + "selects it). Add them to an owner migration and enrol it in "
            + "tools/apply-neon-predeploy-migrations.sh. Allowlisting is NOT an option for any column reachable "
            + "from controller SQL — see Allowlist_NeverCoversAColumnReachableFromControllerSql:\n  "
            + string.Join("\n  ", missing.Select(p => $"{p.Table}.{p.Column}")));
    }

    [Fact]
    public void EverySchemaServiceTable_IsCreatedByTheEnrolledMigrationChain()
    {
        var runtime = SchemaServiceDeclarations();
        var chain = EnrolledMigrationIndex();
        var controllerSql = ControllerSqlCorpus();

        var missing = runtime.Tables
            .Where(t => !chain.Tables.Contains(t))
            .Where(t => !KnownRuntimeOnlyTables.Contains(t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        // Tables a request actually reaches are 42P01s, not cosmetic drift. Surface them
        // first in the message so the migration packet can triage by blast radius.
        var reached = missing.Where(t => ReferencedInSql(controllerSql, t)).ToArray();

        Assert.True(missing.Length == 0,
            $"{missing.Length} tables are declared ONLY by a runtime *SchemaService and are created by no "
            + $"enrolled migration — they cannot exist in any protected environment. {reached.Length} of them "
            + "are referenced by controller SQL and therefore 42P01 on a live request:\n  REACHED BY CONTROLLER SQL: "
            + string.Join(", ", reached)
            + "\n  ALL: " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Allowlist_IsStillAccurate_RemoveEntriesOnceEnrolled()
    {
        var runtime = SchemaServiceDeclarations();
        var chain = EnrolledMigrationIndex();

        foreach (var entry in KnownRuntimeOnlyColumns)
        {
            // An allowlist entry no schema service declares any more is stale.
            Assert.True(runtime.Pairs.Contains(entry),
                $"Allowlist entry {entry.Table}.{entry.Column} is no longer declared by any schema service — remove it.");
            // An entry that has since gained an enrolled migration is stale too:
            // shrinking the list is the point, keep it honest. This is now matched
            // TABLE-QUALIFIED — the previous `\bcolumn\b` corpus scan let an entry look
            // "enrolled" because an unrelated table happened to own the same name.
            Assert.False(chain.Pairs.Contains(entry),
                $"Allowlist entry {entry.Table}.{entry.Column} now has an enrolled migration — remove it from the allowlist.");
        }

        foreach (var table in KnownRuntimeOnlyTables)
        {
            Assert.True(runtime.Tables.Contains(table),
                $"Allowlisted runtime-only table {table} is no longer declared by any schema service — remove it.");
            Assert.False(chain.Tables.Contains(table),
                $"Allowlisted runtime-only table {table} now has an enrolled migration — remove it from the allowlist.");
        }
    }

    // ── The rule that replaces the old prose rationale ────────────────────────────
    // The previous allowlist justified 83 entries with the sentence "neither reachable
    // by production requests today". That was measurably FALSE: at
    // RETEST-20260821-1035-R1, 58 of the 83 entries name a column that appears in a SQL
    // string literal inside backend-dotnet/Controllers/*.cs. Prose cannot be re-checked
    // by CI, so it is replaced by a mechanical prohibition:
    //
    //   A (table,column) pair may NOT be allowlisted if its column name appears in any
    //   SQL string literal in backend-dotnet/Controllers/*.cs.
    //
    // Deliberately column-name-based rather than table-qualified: SQL in this codebase
    // is assembled from concatenated and interpolated fragments, so a table-qualified
    // match under-reports (46 rather than 58 entries), and the safe direction for a
    // PROHIBITION is to over-refuse. The escape hatch is to make the entry unnecessary
    // by enrolling a migration — never to widen this rule.
    [Fact]
    public void Allowlist_NeverCoversAColumnReachableFromControllerSql()
    {
        var controllerSql = ControllerSqlCorpus();
        Assert.True(controllerSql.Length > 200_000,
            $"controller SQL-literal extraction collapsed: {controllerSql.Length} chars");

        var violations = KnownRuntimeOnlyColumns
            .Where(p => ReferencedInSql(controllerSql, p.Column))
            .OrderBy(p => p.Table, StringComparer.Ordinal).ThenBy(p => p.Column, StringComparer.Ordinal)
            .ToArray();

        var clusters = violations
            .GroupBy(p => p.Table)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}({g.Count()})");

        Assert.True(violations.Length == 0,
            $"{violations.Length} of {KnownRuntimeOnlyColumns.Count} allowlist entries name a column that "
            + "controller SQL reads or writes. Each is a live 42703 in a protected environment, so the "
            + "allowlist is certifying a falsehood. The ONLY legal repair is to enrol an owner migration that "
            + "creates the column and then DELETE the entry — never to relax this rule.\n  clusters: "
            + string.Join(" ", clusters)
            + "\n  " + string.Join("\n  ", violations.Select(p => $"{p.Table}.{p.Column}")));

        // Same prohibition, one level up: a runtime-only TABLE that controller SQL reads
        // is a 42P01 and may never be exempted either.
        var tableViolations = KnownRuntimeOnlyTables
            .Where(t => ReferencedInSql(controllerSql, t))
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();
        Assert.True(tableViolations.Length == 0,
            "Allowlisted runtime-only tables that controller SQL reads (42P01 in production — enrol a "
            + "migration, do not exempt):\n  " + string.Join("\n  ", tableViolations));
    }

    // Three-way contract for the Stage86/87 repair itself, in the
    // TenantProvisioningMigrationContractTests style: migration file, runner
    // enrollment, and readiness contract must all agree.
    [Fact]
    public void Stage86And87_AreEnrolledEverywhereTheContractRequires()
    {
        var stage86 = Read("database", "migrations", "2026_08_22_stage86_runtime_route_column_contract.sql");
        foreach (var required in new[]
        {
            "ALTER TABLE public.routes", "sla_risk VARCHAR(60) NOT NULL DEFAULT 'Low'",
            "ALTER TABLE public.work_orders", "ALTER TABLE public.maintenance_items",
            "asset_id BIGINT NULL",
            "ALTER TABLE public.safety_events", "ALTER TABLE public.dashcam_events",
            "event_number VARCHAR(80) NULL",
            "severity VARCHAR(40) NOT NULL DEFAULT 'Info'",
            "module_key VARCHAR(100) NULL",
            "action_type VARCHAR(80) NOT NULL DEFAULT 'update'",
            "'SAFE-' || LPAD(id::TEXT, 5, '0')",
            "2026_08_22_stage86_runtime_route_column_contract",
        })
            Assert.Contains(required, stage86, StringComparison.Ordinal);

        var stage87 = Read("database", "migrations", "2026_08_22_stage87_user_role_id_backfill.sql");
        foreach (var required in new[]
        {
            "role_id IS NULL",
            "r.name = u.role_name",
            "r.company_id IS NULL OR r.company_id = u.company_id",
            "ORDER BY r.company_id NULLS LAST",
            "LIMIT 1",
            "2026_08_22_stage87_user_role_id_backfill",
        })
            Assert.Contains(required, stage87, StringComparison.Ordinal);

        var runner = Read("tools", "apply-neon-predeploy-migrations.sh");
        Assert.Contains("2026_08_21_stage83_platform_settings", runner, StringComparison.Ordinal);
        Assert.Contains("2026_08_22_stage86_runtime_route_column_contract", runner, StringComparison.Ordinal);
        Assert.Contains("2026_08_22_stage87_user_role_id_backfill", runner, StringComparison.Ordinal);

        var readiness = Read("backend-dotnet", "Services", "FleetProductionReadinessService.cs");
        foreach (var required in new[]
        {
            "('routes','sla_risk','character varying(60)',true,'''Low''::character varying','')",
            "('work_orders','asset_id','bigint',false,'','')",
            "('maintenance_items','asset_id','bigint',false,'','')",
            "('safety_events','event_number','character varying(80)',false,'','')",
            "('dashcam_events','event_number','character varying(80)',false,'','')",
            "('audit_logs','severity','character varying(40)',true,'''Info''::character varying','')",
            "('audit_logs','module_key','character varying(100)',false,'','')",
            "('audit_logs','action_type','character varying(80)',true,'''update''::character varying','')",
        })
            Assert.Contains(required, readiness, StringComparison.Ordinal);
    }

    // ── runtime schema-service extraction ────────────────────────────────────────

    internal sealed record SchemaIndex(HashSet<(string Table, string Column)> Pairs, HashSet<string> Tables);

    // ── shared with MigrationRunnerEnrollmentParityTests ─────────────────────────
    // Both tests need the SAME definition of "what the enrolled chain creates";
    // two divergent copies would be two different oracles.
    internal static HashSet<string> EnrolledTables() => EnrolledMigrationIndex().Tables;

    internal static IEnumerable<string> CreateTableNames(string sql)
        => CreateTableDeclarations(StripSqlComments(sql)).Select(d => d.Table);

    internal static IEnumerable<string> SqlLiteralsIn(string csharpSource)
        => StringLiterals(csharpSource)
            .Where(l => l.Length >= 12 && Regex.IsMatch(l,
                @"\b(SELECT|INSERT\s+INTO|UPDATE|DELETE\s+FROM|FROM|JOIN|SET|WHERE|VALUES|ON\s+CONFLICT|COALESCE|GROUP\s+BY|ORDER\s+BY|RETURNING|HAVING)\b",
                RegexOptions.IgnoreCase));

    private static SchemaIndex? _runtimeCache;

    // Derived from a GLOB over every backend-dotnet/Services/*SchemaService.cs — never a
    // hand-listed subset. Four declaration shapes are in use across the 48 files:
    //   a) ColumnDefinition[] Columns = [ new("t","c","DEF"), … ]      (Batch1-7, Trip, Telemetry)
    //   b) var cols = new[] { ("t","c","DEF"), … }                     (DriverSchemaService)
    //   c) EnsureColumnAsync("t","c",…) / inline ALTER TABLE t ADD COLUMN [IF NOT EXISTS] c
    //   d) CREATE TABLE [IF NOT EXISTS] t ( c TYPE, … )                (most of the rest)
    // (d) contributes BOTH the table and its column list: a table the runtime creates and
    // a migration also creates can still carry runtime-only columns.
    private static SchemaIndex SchemaServiceDeclarations()
    {
        if (_runtimeCache is not null) return _runtimeCache;

        var pairs = new HashSet<(string, string)>();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var files = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "backend-dotnet", "Services"), "*SchemaService.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();
        Assert.True(files.Length >= 40, $"schema-service glob found only {files.Length} files — expected 48");

        foreach (var file in files)
        {
            // Comments first: several services document DDL in prose ("CREATE TABLE alone
            // defines …"), which a naive scan reads as a declaration.
            var source = StripCSharpComments(File.ReadAllText(file));

            // ("table", "name", "third") is used for BOTH column definitions and index
            // definitions — ("dispatch_assignments","idx_da_driver","driver_id") — and
            // for unrelated seed tuples ("IE","Ireland","EUR"). A column declaration is
            // the one whose third element opens with a SQL TYPE, so that is the test.
            // Without it the missing-column report fills with 60+ phantom "columns"
            // named idx_*, which is a fabricated defect, not a detected one.
            foreach (Match m in Regex.Matches(source,
                         @"(?:new)?\(\s*""([A-Za-z0-9_]+)""\s*,\s*""([A-Za-z0-9_]+)""\s*,\s*[@$]?""([^""]*)"""))
            {
                if (!IsSqlTypeDeclaration(m.Groups[3].Value)) continue;
                pairs.Add((Lower(m.Groups[1].Value), Lower(m.Groups[2].Value)));
            }
            foreach (Match m in Regex.Matches(source, @"EnsureColumnAsync\(\s*""([A-Za-z0-9_]+)""\s*,\s*""([A-Za-z0-9_]+)"""))
                pairs.Add((Lower(m.Groups[1].Value), Lower(m.Groups[2].Value)));
            // (?!IF\b) stops the optional IF-NOT-EXISTS group from backtracking and
            // capturing the literal "IF" when the real column name is interpolated
            // (FeatureFlagSchemaService: ADD COLUMN IF NOT EXISTS {col} {def}). Those
            // interpolated sites are covered by the tuple array that drives them.
            foreach (Match m in Regex.Matches(source,
                         @"ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?([A-Za-z0-9_\.]+)\s+ADD\s+COLUMN\s+(?:IF\s+NOT\s+EXISTS\s+)?(?!IF\b)([A-Za-z0-9_]+)",
                         RegexOptions.IgnoreCase))
            {
                var table = NormalizeTable(m.Groups[1].Value);
                if (table is null) continue;
                pairs.Add((table, Lower(m.Groups[2].Value)));
            }

            foreach (var (table, columns) in CreateTableDeclarations(source))
            {
                tables.Add(table);
                foreach (var column in columns) pairs.Add((table, column));
            }
        }

        return _runtimeCache = new SchemaIndex(pairs, tables);
    }

    // ── enrolled-migration index ─────────────────────────────────────────────────

    private static SchemaIndex? _chainCache;

    // The chain a protected environment actually receives, in the order ci.yml's
    // dotnet-integration-tests job applies it:
    //   database/init/001_schema.sql + 002_seed.sql + 004_jobs_execution.sql,
    //   the two RLS preflight files the runner hard-requires,
    //   every entry of the runner's MIGRATIONS array (telematics/* included),
    //   the terminal cutover files the runner applies with -f after the array.
    // Everything else in database/migrations/ is applied by nothing — see
    // MigrationRunnerEnrollmentParityTests.UnenrolledOrphans.
    private static SchemaIndex EnrolledMigrationIndex()
    {
        if (_chainCache is not null) return _chainCache;

        var root = RepoRoot();
        var files = new List<string>
        {
            Path.Combine(root, "database", "init", "001_schema.sql"),
            Path.Combine(root, "database", "init", "002_seed.sql"),
            Path.Combine(root, "database", "init", "004_jobs_execution.sql"),
            Path.Combine(root, "database", "migrations", "2026_06_30_stage19_row_level_security.sql"),
            Path.Combine(root, "database", "migrations", "2026_07_01_stage22_rls_reconcile_coverage.sql"),
        };

        var runner = File.ReadAllText(Path.Combine(root, "tools", "apply-neon-predeploy-migrations.sh"));
        var array = Regex.Match(runner, @"MIGRATIONS=\((.*?)\n\)", RegexOptions.Singleline);
        Assert.True(array.Success, "MIGRATIONS array not found in apply-neon-predeploy-migrations.sh — update this test's parser.");
        var entries = array.Groups[1].Value
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToArray();
        Assert.True(entries.Length >= 60, $"MIGRATIONS array parse collapsed: {entries.Length} entries");
        foreach (var entry in entries)
            files.Add(entry.StartsWith("../", StringComparison.Ordinal)
                ? Path.Combine(root, "database", entry[3..].Replace('/', Path.DirectorySeparatorChar) + ".sql")
                : Path.Combine(root, "database", "migrations", entry.Replace('/', Path.DirectorySeparatorChar) + ".sql"));

        foreach (Match m in Regex.Matches(runner, @"-f (database/migrations/[A-Za-z0-9_./]+\.sql)"))
            files.Add(Path.Combine(root, m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar)));

        var pairs = new HashSet<(string, string)>();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files.Distinct(StringComparer.Ordinal))
        {
            Assert.True(File.Exists(file), $"enrolled migration file missing on disk: {file}");
            IndexMigrationSql(File.ReadAllText(file), pairs, tables);
        }

        return _chainCache = new SchemaIndex(pairs, tables);
    }

    private static void IndexMigrationSql(string raw, HashSet<(string, string)> pairs, HashSet<string> tables)
    {
        var sql = StripSqlComments(raw);

        foreach (var (table, columns) in CreateTableDeclarations(sql))
        {
            tables.Add(table);
            foreach (var column in columns) pairs.Add((table, column));
        }

        // CREATE TABLE … AS / PARTITION OF / (LIKE …) and views: the object exists even
        // though its column list is not spelled out inline.
        foreach (Match m in Regex.Matches(sql,
                     @"CREATE\s+(?:UNLOGGED\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?([A-Za-z0-9_""\.]+)\s+(?:AS|PARTITION|LIKE)\b",
                     RegexOptions.IgnoreCase))
        {
            var t = NormalizeTable(m.Groups[1].Value);
            if (t is not null) tables.Add(t);
        }
        foreach (Match m in Regex.Matches(sql,
                     @"CREATE\s+(?:OR\s+REPLACE\s+)?(?:MATERIALIZED\s+)?VIEW\s+(?:IF\s+NOT\s+EXISTS\s+)?([A-Za-z0-9_""\.]+)",
                     RegexOptions.IgnoreCase))
        {
            var t = NormalizeTable(m.Groups[1].Value);
            if (t is not null) tables.Add(t);
        }

        // ALTER TABLE t <actions…>: one statement may carry several comma-separated
        // ADD COLUMN clauses, and this is where table AND column must be read TOGETHER.
        foreach (Match m in Regex.Matches(sql,
                     @"ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?([A-Za-z0-9_""\.]+)([^;]*)",
                     RegexOptions.IgnoreCase))
        {
            var table = NormalizeTable(m.Groups[1].Value);
            if (table is null) continue;
            var actions = m.Groups[2].Value;
            foreach (Match a in Regex.Matches(actions, @"ADD\s+COLUMN\s+(?:IF\s+NOT\s+EXISTS\s+)?([A-Za-z0-9_""]+)", RegexOptions.IgnoreCase))
                pairs.Add((table, Lower(a.Groups[1].Value.Trim('"'))));
            foreach (Match a in Regex.Matches(actions, @"RENAME\s+COLUMN\s+[A-Za-z0-9_""]+\s+TO\s+([A-Za-z0-9_""]+)", RegexOptions.IgnoreCase))
                pairs.Add((table, Lower(a.Groups[1].Value.Trim('"'))));
        }
    }

    // ── shared SQL parsing ───────────────────────────────────────────────────────

    private static IEnumerable<(string Table, List<string> Columns)> CreateTableDeclarations(string sql)
    {
        foreach (Match m in Regex.Matches(sql,
                     @"CREATE\s+(?:UNLOGGED\s+|TEMP\s+|TEMPORARY\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?([A-Za-z0-9_""\.]+)\s*\(",
                     RegexOptions.IgnoreCase))
        {
            var table = NormalizeTable(m.Groups[1].Value);
            if (table is null) continue;

            var open = m.Index + m.Length - 1;
            var close = MatchingParen(sql, open);
            var body = sql[(open + 1)..close];

            var columns = new List<string>();
            foreach (var part in SplitTopLevel(body))
            {
                var trimmed = part.TrimStart();
                var name = Regex.Match(trimmed, @"^([A-Za-z_][A-Za-z0-9_]*)\s");
                if (!name.Success) continue;
                var column = Lower(name.Groups[1].Value);
                if (column is "primary" or "unique" or "constraint" or "foreign" or "check" or "exclude" or "like") continue;
                columns.Add(column);
            }
            yield return (table, columns);
        }
    }

    private static int MatchingParen(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return s.Length - 1;
    }

    private static IEnumerable<string> SplitTopLevel(string body)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '(') depth++;
            else if (body[i] == ')') depth--;
            else if (body[i] == ',' && depth == 0)
            {
                yield return body[start..i];
                start = i + 1;
            }
        }
        yield return body[start..];
    }

    private static string? NormalizeTable(string raw)
    {
        var t = Lower(raw.Trim().Trim('"'));
        if (t.StartsWith("public.", StringComparison.Ordinal)) t = t[7..];
        t = t.Trim('"');
        // Interpolated / schema-qualified names outside public are not part of the
        // tenant surface this test governs.
        return t.Length == 0 || t.Contains('{') || t.Contains('.') ? null : t;
    }

    private static string Lower(string s) => s.ToLowerInvariant();

    private static bool IsSqlTypeDeclaration(string definition) => Regex.IsMatch(definition,
        @"^\s*(BIGINT|BIGSERIAL|SERIAL|SMALLINT|SMALLSERIAL|INTEGER|INT|VARCHAR|CHARACTER|CHAR|CITEXT|TEXT|BOOLEAN|BOOL|NUMERIC|DECIMAL|MONEY|DOUBLE|REAL|FLOAT|DATE|TIMESTAMPTZ|TIMESTAMP|TIMETZ|TIME|INTERVAL|JSONB|JSON|UUID|BYTEA|INET|CIDR|MACADDR|XML|TSVECTOR|ARRAY)\b",
        RegexOptions.IgnoreCase);

    private static string StripSqlComments(string sql)
    {
        sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(sql, @"--[^\n]*", " ");
    }

    // ── controller SQL corpus (the reachability oracle) ──────────────────────────

    private static string? _controllerSqlCache;

    // Only SQL STRING LITERALS, never raw C#: matching a bare identifier across whole
    // controller files would count DTO property names and JSON keys as database reads.
    private static string ControllerSqlCorpus()
    {
        if (_controllerSqlCache is not null) return _controllerSqlCache;

        var sb = new StringBuilder();
        var files = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "backend-dotnet", "Controllers"), "*.cs", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal);
        foreach (var file in files)
            foreach (var literal in StringLiterals(File.ReadAllText(file)))
                // SQL in this codebase is assembled from fragments; a clause like
                // ", arrived_at=COALESCE(arrived_at, NOW())" is a live write with no
                // SELECT/FROM in it, so the keyword set has to cover clause fragments too.
                if (literal.Length >= 12 &&
                    Regex.IsMatch(literal,
                        @"\b(SELECT|INSERT\s+INTO|UPDATE|DELETE\s+FROM|FROM|JOIN|SET|WHERE|VALUES|ON\s+CONFLICT|COALESCE|GROUP\s+BY|ORDER\s+BY|RETURNING|HAVING)\b",
                        RegexOptions.IgnoreCase))
                    sb.Append(literal).Append('\n');
        return _controllerSqlCache = sb.ToString();
    }

    private static bool ReferencedInSql(string corpus, string identifier)
        => Regex.IsMatch(corpus, $@"\b{Regex.Escape(identifier)}\b", RegexOptions.IgnoreCase);

    // ── C# lexing (shared shape with EndpointMappingsSecurityHardeningTests) ─────

    private static IEnumerable<string> StringLiterals(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                var nl = s.IndexOf('\n', i);
                i = nl < 0 ? s.Length : nl;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? s.Length : end + 2;
                continue;
            }
            if (c == '"')
            {
                var verbatim = i > 0 && (s[i - 1] == '@' || (i > 1 && s[i - 1] == '$' && s[i - 2] == '@'));
                var close = SkipStringLiteral(s, i, verbatim);
                yield return s[(i + 1)..Math.Min(close, s.Length)];
                i = close + 1;
                continue;
            }
            if (c == '\'')
            {
                var close = SkipCharLiteral(s, i);
                i = close == i ? i + 1 : close + 1;
                continue;
            }
            i++;
        }
    }

    private static int SkipStringLiteral(string s, int start, bool verbatim)
    {
        var j = start + 1;
        if (verbatim)
        {
            while (j < s.Length)
            {
                if (s[j] == '"')
                {
                    if (j + 1 < s.Length && s[j + 1] == '"') { j += 2; continue; }
                    return j;
                }
                j++;
            }
            return s.Length - 1;
        }
        while (j < s.Length && s[j] != '"')
        {
            if (s[j] == '\\') j++;
            j++;
        }
        return Math.Min(j, s.Length - 1);
    }

    // ' opens a CHAR literal in C#, never a string. Only 'x', '\x', '\uXXXX' shapes
    // count; anything else (an apostrophe inside a comment, e.g. "the caller's tenant")
    // is an ordinary character and must not swallow the rest of the file.
    private static int SkipCharLiteral(string s, int start)
    {
        var j = start + 1;
        if (j >= s.Length) return start;
        if (s[j] == '\\')
        {
            j++;
            if (j >= s.Length) return start;
            var esc = s[j];
            if (esc is 'u' or 'x' or 'U')
            {
                var max = esc == 'U' ? 8 : 4;
                j++;
                for (var k = 0; k < max && j < s.Length && Uri.IsHexDigit(s[j]); k++) j++;
            }
            else j++;
        }
        else j++;
        return j < s.Length && s[j] == '\'' ? j : start;
    }

    private static string StripCSharpComments(string s)
    {
        var sb = new StringBuilder(s.Length);
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"')
            {
                var verbatim = i > 0 && (s[i - 1] == '@' || (i > 1 && s[i - 1] == '$' && s[i - 2] == '@'));
                var close = SkipStringLiteral(s, i, verbatim);
                sb.Append(s[i..Math.Min(close + 1, s.Length)]);
                i = close + 1;
                continue;
            }
            if (c == '\'')
            {
                var close = SkipCharLiteral(s, i);
                if (close != i) { sb.Append(s[i..(close + 1)]); i = close + 1; continue; }
                sb.Append(' ');
                i++;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                var nl = s.IndexOf('\n', i);
                if (nl < 0) { i = s.Length; continue; }
                sb.Append('\n');
                i = nl + 1;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                sb.Append(' ');
                i = end < 0 ? s.Length : end + 2;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    // Allowlist RETIRED. stage88 (2026_08_22_stage88_runtime_schema_service_contract)
    // enrolled every runtime-schema-service declaration as a real migration, so there is
    // no longer any (table, column) that exists only inside a *SchemaService. The set is
    // kept as an empty ratchet: Allowlist_IsStillAccurate_RemoveEntriesOnceEnrolled fails
    // if an entry is ever added back while the chain already creates it, and
    // Allowlist_NeverCoversAColumnReachableFromControllerSql forbids allowlisting any
    // column that controller SQL references. Both make this list shrink-only.
    private static readonly HashSet<(string Table, string Column)> KnownRuntimeOnlyColumns = new();

    // Deliberately EMPTY. Every runtime-only table detected at
    // RETEST-20260821-1035-R1 is reported as a failure rather than exempted: a table no
    // enrolled migration creates is a 42P01, not a documented quirk. Adding an entry
    // here to make the suite green is the exact behaviour this packet exists to remove.
    private static readonly HashSet<string> KnownRuntimeOnlyTables = new(StringComparer.Ordinal);
}
