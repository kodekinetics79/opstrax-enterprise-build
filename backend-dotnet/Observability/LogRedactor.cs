using System.Text;
using System.Text.Json;
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

        // Replace a sensitive JSON property's entire value, not only a quoted scalar. Connector
        // credential containers legitimately contain arrays/objects with arbitrary leaf names;
        // masking only keyed leaves would let {"tokens":["live-secret"]} survive a log render.
        var s = ScrubSensitiveJsonValues(input);
        s = BearerRegex().Replace(s, "Bearer " + Mask);
        s = ConnStringRegex().Replace(s, m => m.Groups[1].Value + "=" + Mask);
        // JSON payloads use quoted property names/values, which the generic key=value
        // matcher deliberately does not consume. Scrub them first so a serialized nested
        // connector config (including Samsara apiToken) is safe in exception/log text.
        s = JsonSecretRegex().Replace(s, m => m.Groups[1].Value + Mask + m.Groups[3].Value);
        s = KeyValueSecretRegex().Replace(s, m => m.Groups[1].Value + m.Groups[2].Value + Mask); // key + sep + mask; drops group3 value
        s = JwtRegex().Replace(s, Mask);
        s = EmailRegex().Replace(s, MaskEmail);
        s = CardRegex().Replace(s, Mask);
        return s;
    }

    private static readonly HashSet<string> SensitiveJsonProperties = new(StringComparer.Ordinal)
    {
        "authtoken", "apitoken", "apikey", "token", "secret", "clientsecret", "password",
        "privatekey", "hmacsecret", "accesstoken", "refreshtoken", "webhooksecret",
        "apikeysecret", "authsecret", "bearertoken", "consumersecret", "secretkey",
        "signingkey", "signingsecret", "connectionstring", "passphrase", "authorization",
        "credential", "credentials", "tokens", "apikeys",
    };

    private static string ScrubSensitiveJsonValues(string input)
    {
        StringBuilder? output = null;
        var copiedThrough = 0;
        var cursor = 0;

        while (cursor < input.Length)
        {
            if (input[cursor] != '"' || !TryReadJsonString(input, cursor, out var propertyEnd))
            {
                cursor++;
                continue;
            }

            var colon = propertyEnd;
            while (colon < input.Length && char.IsWhiteSpace(input[colon])) colon++;
            if (colon >= input.Length || input[colon] != ':')
            {
                cursor = propertyEnd;
                continue;
            }

            string propertyName;
            try
            {
                propertyName = JsonSerializer.Deserialize<string>(
                    input.Substring(cursor, propertyEnd - cursor)) ?? string.Empty;
            }
            catch (JsonException)
            {
                cursor = propertyEnd;
                continue;
            }

            if (!SensitiveJsonProperties.Contains(NormalizeJsonProperty(propertyName)))
            {
                cursor = propertyEnd;
                continue;
            }

            var valueStart = colon + 1;
            while (valueStart < input.Length && char.IsWhiteSpace(input[valueStart])) valueStart++;
            if (!TryReadJsonValue(input, valueStart, out var valueEnd))
            {
                cursor = propertyEnd;
                continue;
            }

            output ??= new StringBuilder(input.Length);
            output.Append(input, copiedThrough, valueStart - copiedThrough);
            output.Append('"').Append(Mask).Append('"');
            copiedThrough = valueEnd;
            cursor = valueEnd;
        }

        if (output is null) return input;
        output.Append(input, copiedThrough, input.Length - copiedThrough);
        return output.ToString();
    }

    private static string NormalizeJsonProperty(string propertyName)
    {
        var normalized = new StringBuilder(propertyName.Length);
        foreach (var character in propertyName)
            if (char.IsLetterOrDigit(character))
                normalized.Append(char.ToLowerInvariant(character));
        return normalized.ToString();
    }

    private static bool TryReadJsonString(string input, int start, out int endExclusive)
    {
        endExclusive = start;
        if (start >= input.Length || input[start] != '"') return false;

        for (var cursor = start + 1; cursor < input.Length; cursor++)
        {
            if (input[cursor] == '\\')
            {
                cursor++;
                continue;
            }
            if (input[cursor] != '"') continue;
            endExclusive = cursor + 1;
            return true;
        }
        return false;
    }

    private static bool TryReadJsonValue(string input, int start, out int endExclusive)
    {
        endExclusive = start;
        if (start >= input.Length) return false;

        if (input[start] == '"')
            return TryReadJsonString(input, start, out endExclusive);

        if (input[start] is '{' or '[')
        {
            var depth = 0;
            for (var cursor = start; cursor < input.Length; cursor++)
            {
                if (input[cursor] == '"' && TryReadJsonString(input, cursor, out var stringEnd))
                {
                    cursor = stringEnd - 1;
                    continue;
                }
                if (input[cursor] is '{' or '[') depth++;
                else if (input[cursor] is '}' or ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        endExclusive = cursor + 1;
                        return true;
                    }
                }
            }

            // A malformed/truncated secret container is safer to redact through end-of-message.
            endExclusive = input.Length;
            return true;
        }

        var primitiveEnd = start;
        while (primitiveEnd < input.Length && input[primitiveEnd] is not ',' and not '}' and not ']'
               && !char.IsWhiteSpace(input[primitiveEnd]))
            primitiveEnd++;
        if (primitiveEnd == start) return false;
        endExclusive = primitiveEnd;
        return true;
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
    [GeneratedRegex(@"(?i)\b(password|passwd|pwd|passphrase|secret|secret[_ -]?key|tokens?|authorization|credentials?|bearer[_ -]?token|consumer[_ -]?secret|signing[_ -]?(?:key|secret)|api[_ -]?(?:keys?|key[_ -]?secret|token)|auth[_ -]?(?:token|secret)|client[_ -]?secret|private[_ -]?key|hmac[_ -]?secret|access[_ -]?(?:key|token)|refresh[_ -]?token|webhook[_ -]?secret|connectionstring|conn[_-]?str|pg_connection)\b(\s*[:=]\s*)([^\s;,""']+)")]
    private static partial Regex KeyValueSecretRegex();

    // JSON: "apiToken":"value" / "credentials":{"password":"value"}.
    // Group 1 includes property + opening quote; group 3 is the closing quote.
    [GeneratedRegex("(?i)(\\\"(?:password|passwd|pwd|passphrase|secret|secret[_ -]?key|tokens?|authorization|credentials?|bearer[_ -]?token|consumer[_ -]?secret|signing[_ -]?(?:key|secret)|api[_ -]?(?:keys?|key[_ -]?secret|token)|auth[_ -]?(?:token|secret)|client[_ -]?secret|private[_ -]?key|hmac[_ -]?secret|access[_ -]?(?:key|token)|refresh[_ -]?token|webhook[_ -]?secret|connectionstring|conn[_ -]?str)\\\"\\s*:\\s*\\\")([^\\\"]*)(\\\")")]
    private static partial Regex JsonSecretRegex();

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
