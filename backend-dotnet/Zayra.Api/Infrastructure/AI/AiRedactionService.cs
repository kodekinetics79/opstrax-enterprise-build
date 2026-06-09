using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Zayra.Api.Infrastructure.AI;

public sealed class AiRedactionService
{
    private static readonly Regex SensitiveValuePatterns = new(
        @"(?i)\b(salary|payroll|compensation|gross pay|net pay|basic salary|iban|bank account|account number|passport|national id|emirates id|ssn)\b[:=\-]?\s*([^\r\n,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmailPattern = new(
        @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Redact(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var redacted = SensitiveValuePatterns.Replace(input, m => $"{m.Groups[1].Value}: [REDACTED]");
        redacted = EmailPattern.Replace(redacted, "[REDACTED_EMAIL]");
        return redacted;
    }

    public string Summarize(string input, int maxLength = 180)
    {
        var redacted = Redact(input);
        if (redacted.Length <= maxLength) return redacted;
        return redacted[..Math.Max(0, maxLength - 3)] + "...";
    }

    public string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
