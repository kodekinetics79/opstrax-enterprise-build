using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class AuditService(Database db)
{
    public Task LogAsync(string actionName, string entityName, long? entityId = null, string actor = "system", CancellationToken ct = default)
    {
        return db.ExecuteAsync(
            @"INSERT INTO audit_logs (company_id, actor_user_id, actor_name, action_name, entity_name, entity_id, details_json)
              VALUES (1, NULL, @actor, @actionName, @entityName, @entityId, JSON_OBJECT('source', 'api'))",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@actionName", actionName);
                cmd.Parameters.AddWithValue("@entityName", entityName);
                cmd.Parameters.AddWithValue("@entityId", entityId);
            }, ct);
    }
}
