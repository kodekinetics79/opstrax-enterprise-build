using System.Text;
using System.Text.RegularExpressions;

namespace Opstrax.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// STAGE 88 INVARIANT — migrations are the ONLY schema authority.
//
// Program.cs never runs the runtime *SchemaService DDL any more, and even before
// stage88 it skipped ALL of it whenever the process connected as the restricted
// opstrax_app role under RLS enforcement — always true in staging and production.
// So any (table, column) that exists ONLY in a *SchemaService declaration list is
// a column the deployed code can query but the deployed database cannot hold:
// 42703 at request time with /health/ready still green. Measured before stage88:
// 1,006 such columns across 507 distinct names.
//
// This test re-derives the declaration surface from source on every run and fails
// if a single declared (table, column) has no creator anywhere under database/**.
// It is the invariant that stops the class reopening one column at a time — which
// is exactly how stage86 shipped routes.sla_risk while leaving
// routes.efficiency_score from the SAME CASE expression behind.
//
// RELATIONSHIP TO RuntimeSchemaMigrationParityTests: that suite asserts parity for
// a curated set and is being reworked separately. This one is deliberately
// exhaustive and unkeyed — it enumerates EVERY declaration rather than a list a
// human maintains, so the two overlap on purpose. If they ever disagree, this one
// is the wider net.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class RuntimeSchemaServiceMigrationAuthorityTests
{
    [Fact]
    public void EveryRuntimeSchemaServiceDeclarationHasAMigrationCreator()
    {
        var root = RepoRoot();
        var declared = DeclaredBySchemaServices(Path.Combine(root, "backend-dotnet", "Services"));
        Assert.True(declared.Count > 3_000,
            $"Declaration extraction collapsed — only {declared.Count} (table,column) pairs were parsed out of the " +
            "*SchemaService files. The parser, not the schema, is what regressed.");

        var created = CreatedByDatabaseSql(Path.Combine(root, "database"));
        Assert.True(created.Count > 3_000,
            $"database/** parse collapsed — only {created.Count} (table,column) pairs were parsed.");

        var orphans = declared.Keys.Where(k => !created.Contains(k))
                                   .OrderBy(k => k.Table, StringComparer.Ordinal)
                                   .ThenBy(k => k.Column, StringComparer.Ordinal)
                                   .ToList();
        if (orphans.Count == 0) return;

        var report = new StringBuilder();
        report.AppendLine($"{orphans.Count} runtime schema-service declaration(s) have NO creator under database/**.");
        report.AppendLine("A protected environment can never hold these, so any endpoint selecting one returns 42703/42P01.");
        report.AppendLine("Fix by adding them to a migration (see database/migrations/2026_08_22_stage88_runtime_schema_service_contract.sql),");
        report.AppendLine("never by re-enabling boot-time DDL.");
        foreach (var orphan in orphans.Take(60))
        {
            report.AppendLine($"  {orphan.Table}.{orphan.Column}   declared in {declared[orphan]}");
        }
        if (orphans.Count > 60) report.AppendLine($"  … and {orphans.Count - 60} more");
        Assert.Fail(report.ToString());
    }

    [Fact]
    public void Stage88IsEnrolledInThePredeployRunner()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "apply-neon-predeploy-migrations.sh"));
        // Ordering in that runner is a hand-maintained array, not a filename sort: a
        // migration file enrolled nowhere is applied by nothing.
        Assert.Contains("2026_08_22_stage88_runtime_schema_service_contract", runner, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "database", "migrations",
            "2026_08_22_stage88_runtime_schema_service_contract.sql")));
    }

    [Fact]
    public void BootDoesNotRunRuntimeSchemaDdlByDefault()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "backend-dotnet", "Program.cs"));
        // The retired path must stay retired wherever migrations own the database:
        // an explicit switch, never a role inference, and hard-off in a protected
        // environment or once the stage88 ledger row exists.
        Assert.Contains("SchemaInit:RunRuntimeDdl", program, StringComparison.Ordinal);
        Assert.Contains("ResolveRuntimeSchemaDdlAsync", program, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"if\s*\(IsProtectedEnvironment\(app\.Environment\)\)\s*\{[^}]*return false;"), program);
        Assert.Contains("2026_08_22_stage88_runtime_schema_service_contract", program, StringComparison.Ordinal);
        // The role decision must no longer GATE DDL — it only proves the runtime identity.
        // (The retired name may still appear in prose explaining why it went away.)
        Assert.DoesNotMatch(new Regex(@"ShouldRunSchemaInitAsync\s*\("), program);
        Assert.Contains("AssertRuntimeDatabaseIdentityAsync(app,", program, StringComparison.Ordinal);
    }

    // ── declaration extraction ───────────────────────────────────────────────
    private static readonly Regex RawLiteral = new("\"\"\"(?<v>.*?)\"\"\"", RegexOptions.Singleline);
    private static readonly Regex VerbatimLiteral = new("@\"(?<v>(?:[^\"]|\"\")*)\"", RegexOptions.Singleline);
    private static readonly Regex TripleTuple = new(
        "\\(\\s*\"(?<t>[a-z_][a-z0-9_]*)\"\\s*,\\s*\"(?<c>[a-z_][a-z0-9_]*)\"\\s*,\\s*\"(?<d>[^\"]*)\"\\s*\\)");
    private static readonly Regex SqlTypeStart = new(
        "^(BIGINT|INT|INTEGER|SMALLINT|SERIAL|BIGSERIAL|BOOLEAN|BOOL|TEXT|VARCHAR|CHAR|CHARACTER|NUMERIC|DECIMAL|REAL|" +
        "DOUBLE|FLOAT|DATE|TIME|TIMESTAMP|TIMESTAMPTZ|JSONB|JSON|UUID|BYTEA|INET|CIDR|MACADDR|INTERVAL|MONEY|XML|TSVECTOR)\\b",
        RegexOptions.IgnoreCase);

    private static Dictionary<(string Table, string Column), string> DeclaredBySchemaServices(string servicesDir)
    {
        var declared = new Dictionary<(string, string), string>();
        foreach (var path in Directory.EnumerateFiles(servicesDir, "*SchemaService.cs").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var source = File.ReadAllText(path);

            foreach (Match m in TripleTuple.Matches(StripLineComments(source)))
            {
                var definition = m.Groups["d"].Value.Trim();
                if (!SqlTypeStart.IsMatch(definition)) continue;
                declared.TryAdd((m.Groups["t"].Value, m.Groups["c"].Value), name);
            }

            foreach (var literal in SqlLiterals(source))
            {
                foreach (var pair in ParseSql(literal)) declared.TryAdd(pair, name);
            }
        }
        return declared;
    }

    private static HashSet<(string Table, string Column)> CreatedByDatabaseSql(string databaseDir)
    {
        var created = new HashSet<(string, string)>();
        foreach (var path in Directory.EnumerateFiles(databaseDir, "*.sql", SearchOption.AllDirectories))
        {
            foreach (var pair in ParseSql(File.ReadAllText(path))) created.Add(pair);
        }
        return created;
    }

    private static IEnumerable<string> SqlLiterals(string source)
    {
        foreach (Match m in RawLiteral.Matches(source)) yield return m.Groups["v"].Value;
        foreach (Match m in VerbatimLiteral.Matches(source)) yield return m.Groups["v"].Value.Replace("\"\"", "\"");
    }

    // ── the one SQL parser both sides use ────────────────────────────────────
    private static readonly Regex CreateTable = new(
        @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>(?:[A-Za-z_][A-Za-z0-9_]*\.)?[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.IgnoreCase);
    private static readonly Regex AlterHead = new(
        @"ALTER\s+TABLE\s+(?:IF\s+EXISTS\s+)?(?:ONLY\s+)?(?<table>(?:[A-Za-z_][A-Za-z0-9_]*\.)?[A-Za-z_][A-Za-z0-9_]*)\s+(?=ADD\s+COLUMN)",
        RegexOptions.IgnoreCase);
    private static readonly Regex AddColumn = new(
        @"^ADD\s+COLUMN\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<col>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.IgnoreCase);

    private static readonly string[] ConstraintStarters =
        ["primary", "foreign", "unique", "check", "constraint", "exclude", "like", "inherits"];

    private static IEnumerable<(string Table, string Column)> ParseSql(string rawSql)
    {
        if (rawSql.IndexOf("CREATE TABLE", StringComparison.OrdinalIgnoreCase) < 0 &&
            rawSql.IndexOf("ALTER TABLE", StringComparison.OrdinalIgnoreCase) < 0)
        {
            yield break;
        }
        var sql = StripSqlComments(rawSql);

        foreach (Match m in CreateTable.Matches(sql))
        {
            var body = BalancedBody(sql, m.Index + m.Length - 1);
            if (body is null) continue;
            var table = Unqualify(m.Groups["name"].Value);
            foreach (var part in SplitTopLevel(body))
            {
                var word = Regex.Match(part, "^[A-Za-z_]+");
                if (!word.Success || ConstraintStarters.Contains(word.Value.ToLowerInvariant())) continue;
                var column = word.Value.ToLowerInvariant();
                if (!Regex.IsMatch(column, "^[a-z_][a-z0-9_]*$")) continue;
                yield return (table, column);
            }
        }

        foreach (Match m in AlterHead.Matches(sql))
        {
            var tail = sql[(m.Index + m.Length)..];
            var stop = tail.IndexOf(';');
            if (stop >= 0) tail = tail[..stop];
            var table = Unqualify(m.Groups["table"].Value);
            foreach (var part in SplitTopLevel(tail))
            {
                var add = AddColumn.Match(part.Trim());
                if (!add.Success) continue;
                var column = add.Groups["col"].Value.ToLowerInvariant();
                if (column is "if" or "not" or "exists" or "column" or "table") continue;
                yield return (table, column);
            }
        }
    }

    private static string Unqualify(string identifier)
    {
        var trimmed = identifier.Trim().Trim('"');
        var dot = trimmed.LastIndexOf('.');
        return (dot >= 0 ? trimmed[(dot + 1)..] : trimmed).ToLowerInvariant();
    }

    private static string? BalancedBody(string sql, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '\'')
            {
                i = SkipQuoted(sql, i) - 1;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) return sql[(openIndex + 1)..i];
            }
        }
        return null;
    }

    private static int SkipQuoted(string sql, int start)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == '\'')
            {
                if (i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                return i + 1;
            }
            i++;
        }
        return i;
    }

    private static List<string> SplitTopLevel(string body)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '\'')
            {
                var end = SkipQuoted(body, i);
                current.Append(body, i, end - i);
                i = end - 1;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')') depth--;
            if (c == ',' && depth == 0)
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString().Trim());
        return parts.Where(p => p.Length > 0).ToList();
    }

    private static string StripSqlComments(string sql)
    {
        var output = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '\'')
            {
                var end = SkipQuoted(sql, i);
                output.Append(sql, i, end - i);
                i = end - 1;
                continue;
            }
            var dollar = Regex.Match(sql[i..], @"^\$[A-Za-z_0-9]*\$");
            if (dollar.Success)
            {
                var tag = dollar.Value;
                var close = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                if (close >= 0)
                {
                    output.Append(sql, i, close + tag.Length - i);
                    i = close + tag.Length - 1;
                    continue;
                }
            }
            if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                var newline = sql.IndexOf('\n', i);
                if (newline < 0) break;
                i = newline - 1;
                continue;
            }
            if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                var close = sql.IndexOf("*/", i, StringComparison.Ordinal);
                if (close < 0) break;
                i = close + 1;
                continue;
            }
            output.Append(sql[i]);
        }
        return output.ToString();
    }

    private static string StripLineComments(string source)
    {
        var output = new StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '"')
            {
                var j = i + 1;
                while (j < source.Length)
                {
                    if (source[j] == '\\') { j += 2; continue; }
                    if (source[j] == '"')
                    {
                        if (j + 1 < source.Length && source[j + 1] == '"') { j += 2; continue; }
                        j++;
                        break;
                    }
                    j++;
                }
                output.Append(source, i, Math.Min(j, source.Length) - i);
                i = Math.Min(j, source.Length) - 1;
                continue;
            }
            if (i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
            {
                var newline = source.IndexOf('\n', i);
                if (newline < 0) break;
                i = newline - 1;
                continue;
            }
            output.Append(source[i]);
        }
        return output.ToString();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
