using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Middleware;
using Opstrax.Api.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<Database>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddSingleton<Batch1SchemaService>();
builder.Services.AddSingleton<Batch2SchemaService>();
builder.Services.AddSingleton<Batch3SchemaService>();
builder.Services.AddSingleton<Batch4SchemaService>();
builder.Services.AddSingleton<Batch5SchemaService>();
builder.Services.AddSingleton<Batch6SchemaService>();
builder.Services.AddSingleton<Batch7SchemaService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("OpsTraxCors", policy =>
    {
        var origins = builder.Configuration["Cors:AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? ["http://localhost:10000"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<Batch1SchemaService>().EnsureAsync();
    await scope.ServiceProvider.GetRequiredService<Batch2SchemaService>().EnsureAsync();
    await scope.ServiceProvider.GetRequiredService<Batch3SchemaService>().EnsureAsync();
    await scope.ServiceProvider.GetRequiredService<Batch4SchemaService>().EnsureAsync();
    await scope.ServiceProvider.GetRequiredService<Batch5SchemaService>().EnsureAsync();
    await scope.ServiceProvider.GetRequiredService<Batch6SchemaService>().EnsureAsync();
    await scope.ServiceProvider.GetRequiredService<Batch7SchemaService>().EnsureAsync();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors("OpsTraxCors");
app.UseSwagger();
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase),
    branch =>
    {
        branch.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (string.Equals(path, "/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
                (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                 path.StartsWith("/api/customer-eta/track/", StringComparison.OrdinalIgnoreCase)))
            {
                await next();
                return;
            }

            var authHeader = context.Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(authHeader) ||
                !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized", "Missing bearer token"));
                return;
            }

            var token = authHeader["Bearer ".Length..].Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized", "Invalid bearer token"));
                return;
            }

            var db = context.RequestServices.GetRequiredService<Database>();
            var session = await db.QuerySingleAsync(
                @"SELECT s.user_id, s.company_id, u.role_name, u.role_id, u.permissions_json, r.permissions_json role_permissions_json
                  FROM user_sessions s
                  JOIN users u ON u.id = s.user_id
                  LEFT JOIN roles r ON r.id = u.role_id
                  WHERE s.session_token=@token
                    AND s.expires_at > NOW()
                    AND u.status='Active'
                  LIMIT 1",
                c => c.Parameters.AddWithValue("@token", token));

            if (session is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized", "Session expired or invalid"));
                return;
            }

            var userId = Convert.ToInt64(session["userId"]);
            var companyId = Convert.ToInt64(session["companyId"]);
            var roleName = session["roleName"]?.ToString() ?? string.Empty;
            var roleId = session.TryGetValue("roleId", out var rid) && rid is not null && rid is not DBNull ? Convert.ToInt64(rid) : 0;

            var permissions = ParsePermissions(session.GetValueOrDefault("permissionsJson"))
                .Concat(ParsePermissions(session.GetValueOrDefault("rolePermissionsJson")))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (roleId > 0)
            {
                var rows = await db.QueryAsync(
                    "SELECT permission_key FROM role_permissions WHERE role_id=@roleId",
                    c => c.Parameters.AddWithValue("@roleId", roleId));
                foreach (var row in rows)
                {
                    var key = row.GetValueOrDefault("permissionKey")?.ToString();
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        permissions.Add(key.Trim());
                    }
                }
            }

            if (permissions.Count == 0 && string.Equals(roleName, "Super Admin", StringComparison.OrdinalIgnoreCase))
            {
                permissions.Add("*");
            }

            context.Items[EndpointMappings.AuthUserIdItemKey] = userId;
            context.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
            context.Items[EndpointMappings.AuthRoleItemKey] = roleName;
            context.Items[EndpointMappings.AuthPermissionsItemKey] = permissions.ToArray();

            await next();
        });
    });
app.MapGet("/swagger", () => Results.Content(SwaggerHtml(), "text/html"));
app.MapGet("/swagger/index.html", () => Results.Content(SwaggerHtml(), "text/html"));

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "opstrax-api", utc = DateTime.UtcNow }));
app.MapOpsTraxEndpoints();

app.Run();

static IEnumerable<string> ParsePermissions(object? source)
{
    if (source is null or DBNull) yield break;

    if (source is byte[] bytes)
    {
        source = System.Text.Encoding.UTF8.GetString(bytes);
    }

    if (source is JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in json.EnumerateArray())
        {
            var key = item.GetString();
            if (!string.IsNullOrWhiteSpace(key)) yield return key.Trim();
        }
        yield break;
    }

    if (source is string str && !string.IsNullOrWhiteSpace(str))
    {
        str = str.Trim();
        if (str.StartsWith("[", StringComparison.Ordinal))
        {
            List<string>? values = null;
            try
            {
                values = JsonSerializer.Deserialize<List<string>>(str);
            }
            catch
            {
                yield break;
            }

            if (values is null) yield break;
            foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                yield return value.Trim();
            }
            yield break;
        }

        yield return str;
    }
}

static string SwaggerHtml() => """
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>OpsTrax API Swagger</title>
<style>body{margin:0;background:#f3f6fb;color:#0f172a;font-family:Inter,system-ui,sans-serif}.wrap{max-width:980px;margin:0 auto;padding:48px 24px}.card{border:1px solid #dbe5f0;background:#fff;border-radius:18px;padding:28px;box-shadow:0 1px 2px rgba(15,23,42,.04),0 16px 42px rgba(15,23,42,.08)}a{color:#1d4ed8;font-weight:700}code{background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:3px 6px;color:#0f766e}</style></head>
<body><main class="wrap"><div class="card"><p style="color:#0f766e;font-weight:800;letter-spacing:.18em;text-transform:uppercase">OpsTrax Transport Management Solution</p><h1>OpsTrax API Swagger</h1><p>OpenAPI specification is available at <a href="/swagger/v1/swagger.json">/swagger/v1/swagger.json</a>.</p><p>Core endpoints include <code>/api/command-center/summary</code>, <code>/api/control-tower/summary</code>, <code>/api/vehicles</code>, <code>/api/drivers</code>, <code>/api/jobs</code>, <code>/api/dispatch/board</code>, and <code>/api/ai/ask</code>.</p></div></main></body></html>
""";
