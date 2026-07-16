using System.Globalization;
using System.Text.Json;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// Financial config envelope (ADR-008 P1) endpoints: draft/list/get a config set, upsert typed
// documents, publish (maker-checker, high-risk), archive, read the change log, and resolve the effective
// config. RBAC finance.config.* folds into finance/billing manage.
public static class FinancialConfigEndpoints
{
    public static void MapFinancialConfigEndpoints(this WebApplication app)
    {
        app.MapPost("/api/fin-config", CreateDraft);
        app.MapGet("/api/fin-config", ListSets);
        app.MapGet("/api/fin-config/resolved", Resolved);
        app.MapGet("/api/fin-config/{id:long}", GetSet);
        app.MapPost("/api/fin-config/{id:long}/documents", UpsertDocument);
        app.MapGet("/api/fin-config/{id:long}/documents", GetDocuments);
        app.MapPost("/api/fin-config/{id:long}/publish", Publish);
        app.MapPost("/api/fin-config/{id:long}/archive", Archive);
        app.MapGet("/api/fin-config/{id:long}/change-log", ChangeLog);
    }

    private static async Task<IResult> CreateDraft(HttpContext http, Dictionary<string, object?> body, FinancialConfigService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.config.create") is { } d) return d;
        var userId = Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey] ?? 0L);
        var o = await svc.CreateDraftAsync(EndpointMappings.GetCompanyId(http),
            Str(body, "archetype") ?? "custom", Str(body, "templateKey"), Long(body, "basedOnConfigSetId"),
            Str(body, "title") ?? "Config set", userId, ct);
        return o.Ok ? Results.Ok(ApiResponse<object>.Ok(new { o.ConfigSetId, o.VersionNo, o.Status }, "Config draft created"))
                    : Results.BadRequest(ApiResponse<object>.Fail($"Cannot create: {o.Reason}"));
    }

    private static async Task<IResult> ListSets(HttpContext http, FinancialConfigService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.config.read") is { } d) return d;
        var status = http.Request.Query.TryGetValue("status", out var s) ? s.ToString() : null;
        return Results.Ok(ApiResponse<object>.Ok(await svc.ListConfigSetsAsync(EndpointMappings.GetCompanyId(http), status, ct)));
    }

    private static async Task<IResult> Resolved(HttpContext http, FinancialConfigService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.config.read") is { } d) return d;
        var asOf = http.Request.Query.TryGetValue("asOf", out var a) && DateOnly.TryParse(a, out var ad) ? ad : DateOnly.FromDateTime(DateTime.UtcNow);
        return Results.Ok(ApiResponse<object>.Ok(await svc.GetResolvedAsync(EndpointMappings.GetCompanyId(http), asOf, ct)));
    }

    private static async Task<IResult> GetSet(HttpContext http, long id, FinancialConfigService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.config.read") is { } d) return d;
        var row = await svc.GetConfigSetAsync(EndpointMappings.GetCompanyId(http), id, ct);
        return row is null ? Results.NotFound(ApiResponse<object>.Fail("Config set not found")) : Results.Ok(ApiResponse<object>.Ok(row));
    }

    private static async Task<IResult> UpsertDocument(HttpContext http, long id, Dictionary<string, object?> body, FinancialConfigService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.config.update") is { } d) return d;
        var userId = Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey] ?? 0L);
        var docType = Str(body, "docType"); var docKey = Str(body, "docKey");
        if (docType is null || docKey is null) return Results.BadRequest(ApiResponse<object>.Fail("docType and docKey are required"));
        var content = body.TryGetValue("content", out var cv) && cv is not null ? JsonSerializer.Serialize(cv) : "{}";
        var o = await svc.UpsertDocumentAsync(EndpointMappings.GetCompanyId(http), id, docType, docKey, content, userId, ct);
        return o.Ok ? Results.Ok(ApiResponse<object>.Ok(new { o.Status }, "Document saved"))
                    : Results.BadRequest(ApiResponse<object>.Fail($"Cannot save document: {o.Reason}"));
    }

    private static async Task<IResult> GetDocuments(HttpContext http, long id, FinancialConfigService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.config.read") is { } d) return d;
        return Results.Ok(ApiResponse<object>.Ok(await svc.GetDocumentsAsync(EndpointMappings.GetCompanyId(http), id, ct)));
    }

    private static async Task<IResult> Publish(HttpContext http, long id, Dictionary<string, object?>? body, FinancialConfigService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.config.publish") is { } d) return d;
        var userId = Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey] ?? 0L);
        var eff = body is not null && body.TryGetValue("effectiveFrom", out var e) && DateOnly.TryParse(e?.ToString(), out var ed) ? ed : DateOnly.FromDateTime(DateTime.UtcNow);
        var mode = string.Equals(body is not null && body.TryGetValue("mode", out var m) ? m?.ToString() : null, "commit", StringComparison.OrdinalIgnoreCase)
            ? ConfigPublishMode.Commit : ConfigPublishMode.Preview;
        var o = await svc.PublishAsync(EndpointMappings.GetCompanyId(http), id, eff, userId, mode, ct);
        return o.Published ? Results.Ok(ApiResponse<object>.Ok(new { o.ConfigSetId, o.Status }, "Config set published"))
                           : Results.BadRequest(ApiResponse<object>.Fail($"Not published: {o.Reason}"));
    }

    private static async Task<IResult> Archive(HttpContext http, long id, FinancialConfigService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.config.update") is { } d) return d;
        var userId = Convert.ToInt64(http.Items[EndpointMappings.AuthUserIdItemKey] ?? 0L);
        var o = await svc.ArchiveAsync(EndpointMappings.GetCompanyId(http), id, userId, ct);
        return o.Ok ? Results.Ok(ApiResponse<object>.Ok(new { o.Status }, "Config set archived"))
                    : Results.BadRequest(ApiResponse<object>.Fail($"Cannot archive: {o.Reason}"));
    }

    private static async Task<IResult> ChangeLog(HttpContext http, long id, FinancialConfigService svc, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "finance.config.read") is { } d) return d;
        return Results.Ok(ApiResponse<object>.Ok(await svc.GetChangeLogAsync(EndpointMappings.GetCompanyId(http), id, ct)));
    }

    private static string? Str(Dictionary<string, object?> b, string k) => b.TryGetValue(k, out var v) && v is not null ? v.ToString() : null;
    private static long? Long(Dictionary<string, object?> b, string k) => Str(b, k) is { } s && long.TryParse(s, out var v) ? v : null;
}
