using Zayra.Api.Data;
using Zayra.Api.Models;

namespace Zayra.Api.Infrastructure.Notifications;

public interface INotificationService
{
    Task NotifyAsync(Guid tenantId, Guid? userId, string title, string message, string entityName, string? entityId, CancellationToken cancellationToken);
}

public class NotificationService : INotificationService
{
    private readonly ZayraDbContext _db;

    public NotificationService(ZayraDbContext db)
    {
        _db = db;
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
    }
}
