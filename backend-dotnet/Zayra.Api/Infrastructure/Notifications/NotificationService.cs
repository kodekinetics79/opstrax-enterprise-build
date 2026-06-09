using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;
using Zayra.Api.Infrastructure.Email;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Notifications;

public interface INotificationService
{
    Task NotifyAsync(Guid tenantId, Guid? userId, string title, string message, string entityName, string? entityId, CancellationToken cancellationToken);

    /// <summary>Render and dispatch a named notification template to a specific email address.</summary>
    Task SendEmailAsync(Guid tenantId, string templateCode, string toAddress, string toName, Dictionary<string, string> variables, CancellationToken cancellationToken);
}

public class NotificationService : INotificationService
{
    private readonly ZayraDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(ZayraDbContext db, IEmailService email, ILogger<NotificationService> log)
    {
        _db = db;
        _email = email;
        _log = log;
    }

    public async Task NotifyAsync(Guid tenantId, Guid? userId, string title, string message, string entityName, string? entityId, CancellationToken cancellationToken)
    {
        _db.Notifications.Add(new Notification
        {
            TenantId = tenantId,
            UserId = userId,
            Title = title,
            Message = message,
            EntityName = entityName,
            EntityId = entityId
        });
        await _db.SaveChangesAsync(cancellationToken);

        // If user has an email address, also dispatch email
        if (userId.HasValue)
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.Id == userId.Value)
                .Select(u => new { u.Email, u.FullName })
                .FirstOrDefaultAsync(cancellationToken);

            if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
            {
                var html = $"<p>{System.Web.HttpUtility.HtmlEncode(message)}</p>";
                try
                {
                    await _email.SendAsync(user.Email, user.FullName ?? string.Empty, title, html, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to send notification email to {Email}", user.Email);
                }
            }
        }
    }

    public async Task SendEmailAsync(Guid tenantId, string templateCode, string toAddress, string toName,
        Dictionary<string, string> variables, CancellationToken cancellationToken)
    {
        var template = await _db.NotificationTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Code == templateCode && !t.IsDeleted, cancellationToken);

        string subject, body;
        if (template is not null)
        {
            subject = Interpolate(template.SubjectEn, variables);
            body = Interpolate(template.BodyEn, variables);
        }
        else
        {
            // Fallback: construct a plain email from variables
            subject = variables.TryGetValue("Subject", out var s) ? s : templateCode;
            body = variables.TryGetValue("Body", out var b) ? b : string.Join("<br>", variables.Select(kv => $"<b>{kv.Key}:</b> {kv.Value}"));
        }

        try
        {
            await _email.SendAsync(toAddress, toName, subject, $"<html><body style='font-family:sans-serif'>{body}</body></html>", cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Template email {Code} to {Email} failed", templateCode, toAddress);
        }
    }

    private static string Interpolate(string template, Dictionary<string, string> vars)
    {
        foreach (var kv in vars)
            template = template.Replace($"{{{{{kv.Key}}}}}", kv.Value, StringComparison.OrdinalIgnoreCase);
        return template;
    }
}
