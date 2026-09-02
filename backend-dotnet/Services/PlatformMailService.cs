using System.Net;
using System.Net.Mail;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// Outbound SMTP delivery for platform + tenant onboarding mail (operator invites,
// tenant admin activation, password resets, detention warnings).
//
// Configuration resolves DB-first, environment-second:
//   platform_settings (smtp.host / port / user / password / from / from_name / ssl)
//   → SMTP_HOST / SMTP_PORT / SMTP_USER / SMTP_PASS / SMTP_FROM
//
// The DB layer is what makes mail configurable from the Platform Admin console; the
// env layer keeps every existing Render deployment working with no change. Delivery
// stays best-effort: when SMTP is unconfigured or a send fails, callers fall back to
// the one-time invite link, so email is an enhancement and never a hard dependency.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class PlatformMailService(PlatformSettingsService settings, ILogger<PlatformMailService> logger)
{
    public const string HostKey     = "smtp.host";
    public const string PortKey     = "smtp.port";
    public const string UserKey     = "smtp.username";
    public const string PasswordKey = "smtp.password";
    public const string FromKey     = "smtp.from_address";
    public const string FromNameKey = "smtp.from_name";
    public const string SslKey      = "smtp.enable_ssl";

    public sealed record SmtpConfig(
        string? Host, int Port, string? Username, string? Password,
        string? FromAddress, string? FromName, bool EnableSsl)
    {
        /// <summary>A host and a from-address are the minimum a send needs.</summary>
        public bool IsUsable => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
    }

    public async Task<SmtpConfig> ResolveAsync(CancellationToken ct = default)
    {
        var host = await settings.GetAsync(HostKey, "SMTP_HOST", ct);
        var portText = await settings.GetAsync(PortKey, "SMTP_PORT", ct);
        var user = await settings.GetAsync(UserKey, "SMTP_USER", ct);
        var password = await settings.GetAsync(PasswordKey, "SMTP_PASS", ct);
        var from = await settings.GetAsync(FromKey, "SMTP_FROM", ct);
        var fromName = await settings.GetAsync(FromNameKey, "SMTP_FROM_NAME", ct);
        var sslText = await settings.GetAsync(SslKey, "SMTP_ENABLE_SSL", ct);

        var port = int.TryParse(portText, out var p) && p > 0 ? p : 587;
        // Implicit-TLS submission (465) is the one common port where STARTTLS negotiation must
        // not be attempted; everything else defaults to on unless explicitly disabled.
        var enableSsl = sslText is null
            ? port != 465
            : !(sslText.Equals("false", StringComparison.OrdinalIgnoreCase) || sslText == "0");

        return new SmtpConfig(host, port, user, password, from, fromName ?? "OpsTrax", enableSsl);
    }

    public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
        => (await ResolveAsync(ct)).IsUsable;

    /// <summary>Best-effort send. Returns false (never throws) when unconfigured or delivery fails.</summary>
    public async Task<bool> TrySendAsync(string to, string subject, string textBody, CancellationToken ct = default)
    {
        var (ok, _) = await SendAsync(to, subject, textBody, ct);
        return ok;
    }

    /// <summary>
    /// Send, returning the failure reason as well. The console's "send test email" needs the
    /// actual SMTP error — "it didn't work" is not a diagnosis an operator can act on.
    /// </summary>
    public async Task<(bool Sent, string? Error)> SendAsync(string to, string subject, string textBody, CancellationToken ct = default)
        => await SendWithConfigAsync(await ResolveAsync(ct), to, subject, textBody, ct);

    // How long one SMTP conversation may take before the attempt is reported as a timeout.
    // SmtpClient.Timeout is ignored on the async path, so an unreachable or filtered server
    // would otherwise hang ~100s — longer than the console's HTTP timeout, which is exactly
    // how a "send test" click can appear to vanish. 20s is generous for a healthy relay.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Send using an explicit configuration instead of the stored/env one. This is what lets
    /// the console TEST credentials as typed, before anything is persisted.
    /// </summary>
    public async Task<(bool Sent, string? Error)> SendWithConfigAsync(
        SmtpConfig config, string to, string subject, string textBody, CancellationToken ct = default)
    {
        if (!config.IsUsable)
            return (false, "SMTP is not configured — set at least a host and a from address.");

        try
        {
            using var client = new SmtpClient(config.Host, config.Port)
            {
                EnableSsl = config.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
            };
            if (!string.IsNullOrWhiteSpace(config.Username))
                client.Credentials = new NetworkCredential(config.Username, config.Password ?? "");

            using var message = new MailMessage
            {
                From = string.IsNullOrWhiteSpace(config.FromName)
                    ? new MailAddress(config.FromAddress!)
                    : new MailAddress(config.FromAddress!, config.FromName),
                Subject = subject,
                Body = textBody,
                IsBodyHtml = false,
            };
            message.To.Add(to);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SendTimeout);
            try
            {
                await client.SendMailAsync(message, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (false, $"Timed out after {SendTimeout.TotalSeconds:0}s connecting to {config.Host}:{config.Port} — " +
                               "the server is unreachable from this deployment or the port is filtered.");
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            // Never log the credential; the message and status are what an operator needs.
            var reason = ex is SmtpException smtp ? $"{smtp.StatusCode}: {smtp.Message}" : ex.Message;
            logger.LogWarning(new EventId(0, "platform_mail_send_failed"),
                "SMTP delivery to {Recipient} failed via {Host}:{Port} — {Reason}",
                to, config.Host, config.Port, reason);
            return (false, reason);
        }
    }
}
