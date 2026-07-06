using System.Net;
using System.Net.Mail;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Minimal SMTP delivery for platform operator invites. Environment-configured
// (SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, SMTP_FROM) so it works the same
// way as the other platform bootstrap settings and needs no DI plumbing.
// When SMTP is not configured every send returns false and callers fall back
// to the one-time invite link — email is an enhancement, never a dependency.
// ─────────────────────────────────────────────────────────────────────────────
public static class PlatformMailService
{
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_HOST")) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMTP_FROM"));

    public static async Task<bool> TrySendAsync(string to, string subject, string textBody, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            var host = Environment.GetEnvironmentVariable("SMTP_HOST")!;
            var port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 587;
            var user = Environment.GetEnvironmentVariable("SMTP_USER");
            var pass = Environment.GetEnvironmentVariable("SMTP_PASS");
            var from = Environment.GetEnvironmentVariable("SMTP_FROM")!;

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };
            if (!string.IsNullOrWhiteSpace(user))
                client.Credentials = new NetworkCredential(user, pass ?? "");

            using var message = new MailMessage(from, to, subject, textBody);
            await client.SendMailAsync(message, ct);
            return true;
        }
        catch
        {
            // Delivery is best-effort: the caller still returns the one-time link,
            // and the audit trail records whether the email went out.
            return false;
        }
    }
}
