using Zayra.Api.Application.Auth;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;

namespace Zayra.Api.Infrastructure.Audit;

public class AuditService : IAuditService
{
    private readonly ZayraDbContext _db;

    public AuditService(ZayraDbContext db)
    {
        _db = db;
    }

    public async Task WriteAsync(string action, string entityName, string? entityId, RequestContext context, string? metadata, CancellationToken cancellationToken)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            TenantId = context.TenantId,
            UserId = context.UserId,
            IpAddress = context.IpAddress,
            UserAgent = context.UserAgent,
            Metadata = metadata
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
