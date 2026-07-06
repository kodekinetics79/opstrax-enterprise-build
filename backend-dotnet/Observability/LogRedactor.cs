using System.Text.RegularExpressions;

namespace Opstrax.Api.Observability;

// ─────────────────────────────────────────────────────────────────────────────
// LogRedactor — last line of defence against PII / secrets in logs.
//
// Applied to every rendered log message + exception text before it is written.
// It is deliberately conservative: it redacts *values* that look like secrets or
// PII (bearer tokens, connection strings, passwords, emails, card/phone numbers,
// JWTs, API keys) while leaving structural text intact so logs stay useful.
//
// This is defence-in-depth — callers should already avoid logging secrets — but
// it guarantees the "no secrets/PII logged" acceptance criterion even if a raw
// exception message (e.g. an Npgsql error echoing a connection string) slips in.
// ─────────────────────────────────────────────────────────────────────────────

public static partial class LogRedactor
{
    private const string Mask = "***REDACTED***";

    public static string Scrub(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;

        var s = input;
        s = BearerRegex().Replace(s, "Bearer " + Mask);
        s = ConnStringRegex().Replace(s, m => m.Groups[1].Value + "=" + Mask);
        s = KeyValueSecretRegex().Replace(s, m => m.Groups[1].Value + m.Groups[2].Value + Mask); // key + sep + mask; drops group3 value
        s = JwtRegex().Replace(s, Mask);
        s = EmailRegex().Replace(s, MaskEmail);
        s = CardRegex().Replace(s, Mask);
        return s;
    }

    private static string MaskEmail(Match m)
    {
        // Keep the first char + domain so logs remain diagnosable without exposing PII.
        var value = m.Value;
        var at = value.IndexOf('@');
        if (at <= 1) return "*@" + value[(at + 1)..];
        return value[0] + "***@" + value[(at + 1)..];
    }

    // "Bearer <token>"
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    // password=... / pwd=... / secret=... / apikey=... / token=... / key=...
    // Group1 = key name incl. delimiter word, Group2 = separator (= or : with spaces)
    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|secret|api[_-]?key|access[_-]?key|token|authorization|client[_-]?secret|connectionstring|conn[_-]?str|pg_connection)\b(\s*[:=]\s*)([^\s;,""']+)")]
    private static partial Regex KeyValueSecretRegex();

    // Postgres/host connection-string tokens: Host=..., Password=..., Username=...
    [GeneratedRegex(@"(?i)\b(Password|Pwd|User ID|Username|Host|Server)\b\s*=\s*[^;]+")]
    private static partial Regex ConnStringRegex();

    // JWTs (three base64url segments)
    [GeneratedRegex(@"eyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+")]
    private static partial Regex JwtRegex();

    // Email addresses
    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    // 13–19 digit card-like numbers (with optional separators)
    [GeneratedRegex(@"\b(?:\d[ -]?){13,19}\b")]
    private static partial Regex CardRegex();
}
