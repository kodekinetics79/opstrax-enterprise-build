namespace Opstrax.Api.Services;

/// <summary>
/// Splits a SQL script into executable statements, breaking ONLY on semicolons that
/// appear at the top level of the script. A five-state lexer tracks the constructs a
/// naive single-quote scanner shreds (the Stage-84 failure class: DO $$ ... $$ blocks
/// split at interior semicolons, applying REVOKE-then-GRANT bodies piecewise):
///   normal | 'single-quote' (with '' escape and E'\'' escape-string awareness)
///          | $tag$ dollar-quote (exact-tag exit; bare $$ included)
///          | -- line comment | /* */ block comment (nested).
/// Double-quoted identifiers ("weird;identifier") are also consumed so a quoted
/// semicolon never breaks a statement. Text is preserved verbatim (comments are not
/// stripped); statements are trimmed and empty fragments skipped — the same output
/// semantics the original CoreSchemaService splitter had for splitter-clean scripts
/// such as database/init/001_schema.sql.
/// Transaction control statements (BEGIN; / COMMIT;) are emitted as their own
/// statements, exactly like the original splitter — callers executing statement by
/// statement get per-statement transactions unless they honor BEGIN/COMMIT.
/// </summary>
public static class SqlStatementSplitter
{
    public static IEnumerable<string> Split(string sql)
    {
        var start = 0;
        var i = 0;
        var n = sql.Length;

        while (i < n)
        {
            var c = sql[i];

            if (c == '-' && i + 1 < n && sql[i + 1] == '-')
            {
                i += 2;
                while (i < n && sql[i] != '\n') i++;
            }
            else if (c == '/' && i + 1 < n && sql[i + 1] == '*')
            {
                var depth = 1;
                i += 2;
                while (i < n && depth > 0)
                {
                    if (sql[i] == '/' && i + 1 < n && sql[i + 1] == '*') { depth++; i += 2; }
                    else if (sql[i] == '*' && i + 1 < n && sql[i + 1] == '/') { depth--; i += 2; }
                    else i++;
                }
            }
            else if (c == '\'')
            {
                var isEscapeString = IsEscapeStringPrefix(sql, i);
                i++;
                while (i < n)
                {
                    if (isEscapeString && sql[i] == '\\' && i + 1 < n) { i += 2; continue; }
                    if (sql[i] == '\'')
                    {
                        if (i + 1 < n && sql[i + 1] == '\'') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
            }
            else if (c == '"')
            {
                i++;
                while (i < n)
                {
                    if (sql[i] == '"')
                    {
                        if (i + 1 < n && sql[i + 1] == '"') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
            }
            else if (c == '$' && TryReadDollarTag(sql, i) is { } tag)
            {
                var close = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                i = close < 0 ? n : close + tag.Length;
            }
            else if (c == ';')
            {
                var statement = sql[start..i].Trim();
                if (statement.Length > 0) yield return statement;
                start = i + 1;
                i++;
            }
            else
            {
                i++;
            }
        }

        var tail = sql[start..].Trim();
        if (tail.Length > 0) yield return tail;
    }

    // A quote opens an escape string when written E'...' (or e'...') and the E is not
    // the tail of a longer identifier/word (RATE'x' is not an escape string).
    private static bool IsEscapeStringPrefix(string sql, int quoteIndex)
    {
        if (quoteIndex == 0) return false;
        var prev = sql[quoteIndex - 1];
        if (prev is not ('E' or 'e')) return false;
        if (quoteIndex == 1) return true;
        var before = sql[quoteIndex - 2];
        return !char.IsLetterOrDigit(before) && before != '_' && before != '$';
    }

    // Dollar-quote opener at sql[i]=='$': bare $$ or $tag$ with tag ~ [A-Za-z_][A-Za-z0-9_]*.
    // Returns the full delimiter (including both '$') or null when not an opener ($1, lone $).
    private static string? TryReadDollarTag(string sql, int i)
    {
        if (i + 1 >= sql.Length) return null;
        if (sql[i + 1] == '$') return "$$";
        static bool TagStart(char c) => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_';
        static bool TagBody(char c) => TagStart(c) || c is >= '0' and <= '9';
        if (!TagStart(sql[i + 1])) return null;
        var j = i + 2;
        while (j < sql.Length && TagBody(sql[j])) j++;
        return j < sql.Length && sql[j] == '$' ? sql[i..(j + 1)] : null;
    }
}
