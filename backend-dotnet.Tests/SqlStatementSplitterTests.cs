using System.Text.RegularExpressions;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Non-DB lane. Locks down the repaired boot-time SQL splitter (the Stage-84 defect:
// the previous CoreSchemaService splitter tracked single quotes only, so it shredded
// DO $$ ... $$ bodies at interior semicolons and applied REVOKE-then-GRANT blocks
// piecewise). The new lexer breaks statements ONLY on top-level semicolons.
public sealed class SqlStatementSplitterTests
{
    // (1) A DO $$ ... $$ block with interior semicolons is one statement.
    [Fact]
    public void DoDollarBlock_WithInteriorSemicolons_IsOneStatement()
    {
        const string sql = "DO $$\nBEGIN\n  PERFORM 1;\n  PERFORM 2;\nEND $$;\nSELECT 3;";
        var statements = SqlStatementSplitter.Split(sql).ToArray();
        Assert.Equal(2, statements.Length);
        Assert.StartsWith("DO $$", statements[0]);
        Assert.Contains("PERFORM 1;", statements[0]);
        Assert.EndsWith("END $$", statements[0]);
        Assert.Equal("SELECT 3", statements[1]);
    }

    // (2) A tagged dollar quote containing a nested bare $$ pair exits only on the exact tag.
    [Fact]
    public void TaggedDollarQuote_WithNestedBareDollarQuote_ExitsOnExactTag()
    {
        const string sql = "DO $outer$\nBEGIN\n  EXECUTE $$SELECT 1; SELECT 2;$$;\nEND $outer$;\nSELECT 9;";
        var statements = SqlStatementSplitter.Split(sql).ToArray();
        Assert.Equal(2, statements.Length);
        Assert.Contains("$$SELECT 1; SELECT 2;$$", statements[0]);
        Assert.EndsWith("END $outer$", statements[0]);
        Assert.Equal("SELECT 9", statements[1]);
    }

    // (3) CREATE FUNCTION with a $fn$ body containing semicolons is one statement.
    [Fact]
    public void CreateFunction_TaggedBody_IsOneStatement()
    {
        const string sql =
            "CREATE FUNCTION bump() RETURNS trigger AS $fn$\nBEGIN\n  NEW.updated_at := now();\n  RETURN NEW;\nEND;\n$fn$ LANGUAGE plpgsql;\nSELECT 1;";
        var statements = SqlStatementSplitter.Split(sql).ToArray();
        Assert.Equal(2, statements.Length);
        Assert.Contains("RETURN NEW;", statements[0]);
        Assert.EndsWith("$fn$ LANGUAGE plpgsql", statements[0]);
    }

    // (4) A trigger function plus its CREATE TRIGGER split into exactly two statements.
    [Fact]
    public void TriggerAndFunction_SplitIntoTwoStatements()
    {
        const string sql =
            "CREATE OR REPLACE FUNCTION touch() RETURNS trigger AS $$\nBEGIN\n  NEW.updated_at := now();\n  RETURN NEW;\nEND;\n$$ LANGUAGE plpgsql;\n" +
            "CREATE TRIGGER trg_touch BEFORE UPDATE ON jobs FOR EACH ROW EXECUTE FUNCTION touch();";
        var statements = SqlStatementSplitter.Split(sql).ToArray();
        Assert.Equal(2, statements.Length);
        Assert.StartsWith("CREATE OR REPLACE FUNCTION", statements[0]);
        Assert.StartsWith("CREATE TRIGGER", statements[1]);
    }

    // (5) A semicolon inside a -- line comment does not break the statement.
    [Fact]
    public void LineComment_WithSemicolon_DoesNotBreakStatement()
    {
        const string sql = "SELECT 1 -- trailing; not a break\n+ 2;\nSELECT 3;";
        var statements = SqlStatementSplitter.Split(sql).ToArray();
        Assert.Equal(2, statements.Length);
        Assert.Contains("+ 2", statements[0]);
    }

    // (6) A semicolon inside a /* */ block comment (including nesting) does not break.
    [Fact]
    public void BlockComment_WithSemicolon_DoesNotBreakStatement()
    {
        const string sql = "SELECT /* stop; /* nested; */ still comment; */ 1;\nSELECT 2;";
        var statements = SqlStatementSplitter.Split(sql).ToArray();
        Assert.Equal(2, statements.Length);
        Assert.Contains("still comment;", statements[0]);
        Assert.Equal("SELECT 2", statements[1]);
    }

    // (7) Doubled-quote escapes ('it''s') and E-string backslash escapes (E'\';') stay inside.
    [Fact]
    public void SingleQuoteEscapes_AndEscapeStrings_DoNotBreakStatements()
    {
        const string sql = "SELECT 'it''s; fine';\nSELECT E'\\';' ;\nSELECT 2;";
        var statements = SqlStatementSplitter.Split(sql).ToArray();
        Assert.Equal(3, statements.Length);
        Assert.Equal("SELECT 'it''s; fine'", statements[0]);
        Assert.Equal("SELECT E'\\';'", statements[1]);
        Assert.Equal("SELECT 2", statements[2]);
    }

    // (8) A double-quoted identifier containing a semicolon does not break the statement.
    [Fact]
    public void DoubleQuotedIdentifier_WithSemicolon_DoesNotBreakStatement()
    {
        const string sql = "SELECT \"weird;identifier\" FROM t;\nSELECT 2;";
        var statements = SqlStatementSplitter.Split(sql).ToArray();
        Assert.Equal(2, statements.Length);
        Assert.Contains("\"weird;identifier\"", statements[0]);
    }

    // (9) BEGIN;/COMMIT; semantics: the splitter intentionally emits BEGIN and COMMIT as
    // their own statements (identical to the original splitter). Callers that execute
    // statement-by-statement therefore run each statement in its own implicit transaction
    // unless they honor these markers — which is why migrations with atomicity
    // requirements (Stage 84's REVOKE-then-GRANT DO block) must keep the atomic part
    // inside a single dollar-quoted statement, and why the runner applies whole files
    // via psql instead of the splitter.
    [Fact]
    public void BeginCommit_AreEmittedAsSeparateStatements()
    {
        const string sql = "BEGIN;\nUPDATE t SET x=1;\nCOMMIT;";
        var statements = SqlStatementSplitter.Split(sql).ToArray();
        Assert.Equal(new[] { "BEGIN", "UPDATE t SET x=1", "COMMIT" }, statements);
    }

    // (10) CORPUS: every checked-in SQL script splits into fragments with balanced
    // dollar-quotes, and no content (other than whitespace and statement separators)
    // is lost or duplicated by the round trip.
    [Fact]
    public void Corpus_EveryRepoSqlFile_SplitsWithoutShreddingDollarQuotes()
    {
        var files = CorpusFiles();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var sql = File.ReadAllText(file);
            var fragments = SqlStatementSplitter.Split(sql).ToArray();
            foreach (var fragment in fragments)
                Assert.False(HasUnbalancedDollarQuote(fragment),
                    $"{Path.GetFileName(file)}: fragment has an unbalanced dollar-quote:\n{fragment}");

            // Rejoining preserves total content modulo separators: strip whitespace and
            // semicolons from both sides — nothing else may be lost or duplicated.
            var rejoined = Canonical(string.Concat(fragments));
            Assert.Equal(Canonical(sql), rejoined);
        }
    }

    // The boot path replays database/init/001_schema.sql at every start. That file is
    // splitter-clean today, so the repaired lexer must produce EXACTLY what the old
    // algorithm produced for it — boot behavior is unchanged.
    [Fact]
    public void CoreSchema001_OldAndNewSplitters_AreEquivalent()
    {
        var sql = File.ReadAllText(RepoPath("database", "init", "001_schema.sql"));
        Assert.Equal(LegacySplit(sql).ToArray(), SqlStatementSplitter.Split(sql).ToArray());
    }

    // (11) The defect, demonstrated: on the Stage-84 migration the OLD algorithm shreds
    // the guarded DO $$ ... $$ block (REVOKE and GRANT land in different fragments),
    // while the new splitter keeps the block intact as a single statement.
    [Fact]
    public void Stage84_OldSplitterShredsDoBlock_NewSplitterKeepsItIntact()
    {
        var sql = File.ReadAllText(RepoPath("database", "migrations", "2026_08_21_stage84_driver_hos_runtime_contract.sql"));

        var legacy = LegacySplit(sql).ToArray();
        var legacyDoFragment = Assert.Single(legacy, s => s.Contains("DO $$", StringComparison.Ordinal));
        Assert.DoesNotContain("END $$", legacyDoFragment, StringComparison.Ordinal); // shredded
        // The old algorithm emits the block's REVOKE as its own bare fragment — outside
        // the guard, separated from its compensating GRANT: the piecewise-application bug.
        Assert.Contains(legacy, s => s == "REVOKE ALL ON TABLE public.hos_records FROM opstrax_app");
        Assert.DoesNotContain(legacy, s => s.Contains("REVOKE ALL ON TABLE", StringComparison.Ordinal)
                                           && s.Contains("GRANT SELECT,INSERT,UPDATE,DELETE", StringComparison.Ordinal));

        var repaired = SqlStatementSplitter.Split(sql).ToArray();
        var doBlock = Assert.Single(repaired, s => s.Contains("DO $$", StringComparison.Ordinal));
        Assert.EndsWith("END $$", doBlock);
        Assert.Contains("REVOKE ALL ON TABLE public.hos_records FROM opstrax_app", doBlock, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT,UPDATE,DELETE ON TABLE public.hos_records TO opstrax_app", doBlock, StringComparison.Ordinal);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string RepoPath(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        return Path.Combine([root, .. parts]);
    }

    private static IReadOnlyList<string> CorpusFiles()
    {
        var migrations = Directory.EnumerateFiles(RepoPath("database", "migrations"), "*.sql", SearchOption.AllDirectories);
        var init = Directory.EnumerateFiles(RepoPath("database", "init"), "*.sql", SearchOption.TopDirectoryOnly);
        return migrations.Concat(init).OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    private static readonly Regex DollarTag = new(@"\$[A-Za-z_][A-Za-z0-9_]*\$|\$\$", RegexOptions.Compiled);

    // Pairs each dollar-quote opener with its exact-tag closer; true when an opener
    // has no closer inside the fragment (i.e. the splitter cut through the body).
    private static bool HasUnbalancedDollarQuote(string fragment)
    {
        var i = 0;
        while (i < fragment.Length)
        {
            var open = DollarTag.Match(fragment, i);
            if (!open.Success) return false;
            var close = fragment.IndexOf(open.Value, open.Index + open.Length, StringComparison.Ordinal);
            if (close < 0) return true;
            i = close + open.Length;
        }
        return false;
    }

    private static string Canonical(string sql) =>
        new(sql.Where(c => !char.IsWhiteSpace(c) && c != ';').ToArray());

    // The pre-repair CoreSchemaService.SplitStatements algorithm, verbatim: single-quote
    // tracking only — no dollar-quote, comment, or identifier awareness. Kept here as the
    // fixture that proves the Stage-84 failure class and the equivalence on 001_schema.sql.
    private static IEnumerable<string> LegacySplit(string sql)
    {
        var start = 0;
        var inSingleQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '\'')
            {
                if (inSingleQuote && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
            }
            else if (sql[i] == ';' && !inSingleQuote)
            {
                var statement = sql[start..i].Trim();
                if (statement.Length > 0) yield return statement;
                start = i + 1;
            }
        }

        var tail = sql[start..].Trim();
        if (tail.Length > 0) yield return tail;
    }
}
