using Opstrax.Api.Controllers;
using Opstrax.Api.Infrastructure;
using Opstrax.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Core Services
builder.Services.AddSingleton<Database>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<Batch7SchemaService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:10000"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();
app.UseCors("DefaultCors");

// Run runtime-safe schema and seed initialization
using (var scope = app.Services.CreateScope())
{
    var schemaService = scope.ServiceProvider.GetRequiredService<Batch7SchemaService>();
    await schemaService.EnsureAsync();
}

// Wire up the Batch 1-7 operational endpoints
app.MapOpsTraxEndpoints();

// Platform Health
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "opstrax-dotnet-api", utc = DateTime.UtcNow }));

app.Run();
