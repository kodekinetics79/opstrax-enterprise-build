using Microsoft.AspNetCore.Http;
using NpgsqlTypes;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class AuditService(Database db)
{
    public sealed record BatchEntry(long EntityId, string? DetailsJson);

    // Platform/system sentinel for audit rows that have NO tenant context (true
    // background/system operations). company_id is NOT NULL, and no real company
    // owns id 0, so 0 is a safe "platform" marker — NEVER a real tenant's id.
    private const long PlatformCompanyId = 0;

    // System-level audit path for contexts with no resolvable tenant (background
    // workers, scheduler). Use LogAsync(HttpContext, ...) for any request-scoped
    // action so the real company_id is recorded.
    public Task LogSystemAsync(string actionName, string entityName, long? entityId = null, string actor = "system", CancellationToken ct = default)
    {
        return AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db,
            "audit_logs",
            "id",
            @"INSERT INTO audit_logs (company_id, actor_user_id, actor_name, action_name, entity_name, entity_id, details_json)
              VALUES (@companyId, NULL, @actor, @actionName, @entityName, @entityId, jsonb_build_object('source', 'system'))",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@companyId", PlatformCompanyId);
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@actionName", actionName);
                cmd.Parameters.AddWithValue("@entityName", entityName);
                cmd.Parameters.AddWithValue("@entityId", (object?)entityId ?? DBNull.Value);
            }, ct);
    }

    public Task LogAsync(HttpContext http, string actionName, string entityName, long? entityId = null, string? detailsJson = null, CancellationToken ct = default)
    {
        var (companyId, actorId, actor) = RequestActor(http);

        return AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db,
            "audit_logs",
            "id",
            @"INSERT INTO audit_logs (company_id, actor_user_id, actor_name, action_name, entity_name, entity_id, details_json)
              VALUES (@companyId, @actorId, @actor, @actionName, @entityName, @entityId, COALESCE(@details::jsonb, jsonb_build_object('source', 'api')))",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@companyId", companyId);
                cmd.Parameters.AddWithValue("@actorId", actorId > 0 ? actorId : DBNull.Value);
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@actionName", actionName);
                cmd.Parameters.AddWithValue("@entityName", entityName);
                cmd.Parameters.AddWithValue("@entityId", (object?)entityId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@details", NormalizeDetailsJson(detailsJson));
            }, ct);
    }

    public Task LogBatchAsync(HttpContext http, string actionName, string entityName,
        IReadOnlyList<BatchEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0) return Task.CompletedTask;
        var (companyId, actorId, actor) = RequestActor(http);
        var payload = System.Text.Json.JsonSerializer.Serialize(entries.Select((entry, index) => new
        {
            ordinal = index,
            entity_id = entry.EntityId,
            details_json = NormalizeDetailsJson(entry.DetailsJson) is string details ? details : null
        }));

        return AuditLogSequenceRepair.ExecuteWithSequenceRepairAsync(
            db,
            "audit_logs",
            "id",
            @"INSERT INTO audit_logs
                (company_id,actor_user_id,actor_name,action_name,entity_name,entity_id,details_json)
              SELECT @companyId,@actorId,@actor,@actionName,@entityName,entry.entity_id,
                     COALESCE(entry.details_json::jsonb,jsonb_build_object('source','api'))
                FROM jsonb_to_recordset(@entries::jsonb)
                  AS entry(ordinal INT,entity_id BIGINT,details_json TEXT)
               ORDER BY entry.ordinal",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@companyId", companyId);
                cmd.Parameters.AddWithValue("@actorId", actorId > 0 ? actorId : DBNull.Value);
                cmd.Parameters.AddWithValue("@actor", actor);
                cmd.Parameters.AddWithValue("@actionName", actionName);
                cmd.Parameters.AddWithValue("@entityName", entityName);
                cmd.Parameters.AddWithValue("@entries", NpgsqlDbType.Jsonb, payload);
            }, ct);
    }

    private static (long CompanyId, long ActorId, string Actor) RequestActor(HttpContext http)
    {
        var companyId = EndpointMappings.GetCompanyId(http);
        var supportGrantRef = http.Items.TryGetValue(PlatformImpersonationPolicy.GrantRefItemKey, out var grantRefValue)
            ? grantRefValue?.ToString()
            : null;
        var actorId = supportGrantRef is null
            && http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var userIdValue) && userIdValue is not null
            ? Convert.ToInt64(userIdValue)
            : 0L;
        var actor = supportGrantRef is not null
            ? $"platform-support:{supportGrantRef.Replace("-", string.Empty, StringComparison.Ordinal)}"
            : http.Items.TryGetValue(EndpointMappings.AuthRoleItemKey, out var roleValue) && roleValue is not null
            ? $"{roleValue}:{actorId}"
            : actorId > 0 ? $"user:{actorId}" : "system";
        return (companyId, actorId, actor);
    }

    // Callers pass @details as a JSONB literal, but many pass a plain string (e.g.
    // "proof:156"), which fails @details::jsonb with "invalid input syntax for type json".
    // Normalize: valid JSON passes through; a non-JSON string is wrapped as {"detail":"…"};
    // empty -> NULL (the SQL COALESCE then supplies the default).
    private static object NormalizeDetailsJson(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson)) return DBNull.Value;
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(detailsJson);
            return detailsJson; // already valid JSON
        }
        catch (System.Text.Json.JsonException)
        {
            return System.Text.Json.JsonSerializer.Serialize(new { detail = detailsJson });
        }
    }
}
