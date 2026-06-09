namespace Zayra.Api.Infrastructure.Email;

public record EmailAttachment(string FileName, byte[] Data, string ContentType);

public interface IEmailService
{
    Task SendAsync(string toAddress, string toName, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default);
}
