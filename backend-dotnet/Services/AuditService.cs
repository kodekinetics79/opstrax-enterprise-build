using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Microsoft.AspNetCore.Http;

namespace Opstrax.Api.Services;

public sealed class AuditService(Database db)
{
    public Task LogAsync(string actionName, string entityName, long? entityId = null, string actor = "system", CancellationToken ct = default)
    {
        return db.ExecuteAsync(
            @"INSERT INTO audit_logs (company_id, actor_user_id, actor_name, action_name, entity_name, entity_id, details_json)
              VALUES (1, NULL, @actor, @actionName, @entityName, @entityId, jsonb_build_object('source', 'api'))",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@actionName", actionName);
                cmd.Parameters.AddWithValue("@entityName", entityName);
                cmd.Parameters.AddWithValue("@entityId", entityId);
            }, ct);
    }

    public Task LogAsync(HttpContext http, string actionName, string entityName, long? entityId = null, string? detailsJson = null, CancellationToken ct = default)
    {
        var companyId = EndpointMappings.GetCompanyId(http);
        var actorId = http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var userIdValue) && userIdValue is not null
            ? Convert.ToInt64(userIdValue)
            : 0L;
        var actor = http.Items.TryGetValue(EndpointMappings.AuthRoleItemKey, out var roleValue) && roleValue is not null
            ? $"{roleValue}:{actorId}"
            : actorId > 0 ? $"user:{actorId}" : "system";

        return db.ExecuteAsync(
            @"INSERT INTO audit_logs (company_id, actor_user_id, actor_name, action_name, entity_name, entity_id, details_json)
              VALUES (@companyId, @actorId, @actor, @actionName, @entityName, @entityId, COALESCE(@details, jsonb_build_object('source', 'api')))",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@companyId", companyId);
                cmd.Parameters.AddWithValue("@actorId", actorId > 0 ? actorId : DBNull.Value);
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@actionName", actionName);
                cmd.Parameters.AddWithValue("@entityName", entityName);
                cmd.Parameters.AddWithValue("@entityId", entityId);
                cmd.Parameters.AddWithValue("@details", string.IsNullOrWhiteSpace(detailsJson) ? DBNull.Value : detailsJson);
            }, ct);
    }
}
