using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using Zayra.Api.Data;

namespace Zayra.Api.Infrastructure.Email;

public class SmtpEmailService : IEmailService
{
    private readonly ZayraDbContext _db;
    private readonly ILogger<SmtpEmailService> _log;

    public SmtpEmailService(ZayraDbContext db, ILogger<SmtpEmailService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task SendAsync(string toAddress, string toName, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
    {
        var cfg = await LoadConfigAsync(cancellationToken);
        if (cfg is null)
        {
            _log.LogWarning("SMTP not configured — email to {To} dropped.", toAddress);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(cfg.FromName, cfg.FromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));
        message.Subject = subject;

        var builder = new BodyBuilder { HtmlBody = htmlBody };
        foreach (var att in attachments ?? [])
            builder.Attachments.Add(att.FileName, att.Data, ContentType.Parse(att.ContentType));
        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        var secureOption = cfg.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(cfg.Host, cfg.Port, secureOption, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cfg.Username))
            await client.AuthenticateAsync(cfg.Username, cfg.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        _log.LogInformation("Email sent to {To} — subject: {Subject}", toAddress, subject);
    }

    private async Task<SmtpConfig?> LoadConfigAsync(CancellationToken ct)
    {
        // Load SMTP settings stored as SystemSettings (category = "Email")
        var settings = await _db.SystemSettings
            .AsNoTracking()
            .Where(x => x.Category == "Email")
            .ToListAsync(ct);

        string? Get(string key) => settings.FirstOrDefault(x => x.SettingKey == key)?.SettingValue;

        var host = Get("Smtp.Host");
        if (string.IsNullOrWhiteSpace(host)) return null;
        if (!int.TryParse(Get("Smtp.Port") ?? "587", out var port)) port = 587;

        return new SmtpConfig(
            host,
            port,
            Get("Smtp.Username") ?? string.Empty,
            Get("Smtp.Password") ?? string.Empty,
            Get("Smtp.FromAddress") ?? string.Empty,
            Get("Smtp.FromName") ?? "KynexOne HR",
            (Get("Smtp.UseTls") ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase)
        );
    }

    private record SmtpConfig(string Host, int Port, string Username, string Password, string FromAddress, string FromName, bool UseTls);
}
