using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Middleware;
using Opstrax.Api.Services;

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
app.MapGet("/swagger", () => Results.Content(SwaggerHtml(), "text/html"));
app.MapGet("/swagger/index.html", () => Results.Content(SwaggerHtml(), "text/html"));

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "opstrax-api", utc = DateTime.UtcNow }));
app.MapOpsTraxEndpoints();

app.Run();

static string SwaggerHtml() => """
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>OpsTrax API Swagger</title>
<style>body{margin:0;background:#f3f6fb;color:#0f172a;font-family:Inter,system-ui,sans-serif}.wrap{max-width:980px;margin:0 auto;padding:48px 24px}.card{border:1px solid #dbe5f0;background:#fff;border-radius:18px;padding:28px;box-shadow:0 1px 2px rgba(15,23,42,.04),0 16px 42px rgba(15,23,42,.08)}a{color:#1d4ed8;font-weight:700}code{background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:3px 6px;color:#0f766e}</style></head>
<body><main class="wrap"><div class="card"><p style="color:#0f766e;font-weight:800;letter-spacing:.18em;text-transform:uppercase">OpsTrax Transport Management Solution</p><h1>OpsTrax API Swagger</h1><p>OpenAPI specification is available at <a href="/swagger/v1/swagger.json">/swagger/v1/swagger.json</a>.</p><p>Core endpoints include <code>/api/command-center/summary</code>, <code>/api/control-tower/summary</code>, <code>/api/vehicles</code>, <code>/api/drivers</code>, <code>/api/jobs</code>, <code>/api/dispatch/board</code>, and <code>/api/ai/ask</code>.</p></div></main></body></html>
""";
