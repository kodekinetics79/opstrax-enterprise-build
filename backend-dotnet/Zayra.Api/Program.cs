using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Attendance;
using Zayra.Api.Application.Approvals;
using Zayra.Api.Application.Employees;
using Zayra.Api.Application.Organization;
using Zayra.Api.Data;
using Zayra.Api.Infrastructure.Approvals;
using Zayra.Api.Infrastructure.Audit;
using Zayra.Api.Infrastructure.Auth;
using Zayra.Api.Infrastructure.Attendance;
using Zayra.Api.Infrastructure.Notifications;
using Zayra.Api.Infrastructure.Localization;
using Zayra.Api.Infrastructure.Documents;
using Zayra.Api.Infrastructure.Employees;
using Zayra.Api.Infrastructure.Organization;
using Zayra.Api.Infrastructure.Seed;
using Zayra.Api.Application.Recruitment;
using Zayra.Api.Infrastructure.Recruitment;
using Zayra.Api.Application.Performance;
using Zayra.Api.Infrastructure.Performance;
using Zayra.Api.Application.Leave;
using Zayra.Api.Infrastructure.Leave;
using Zayra.Api.Application.Common;
using Zayra.Api.Infrastructure.Common;
using Zayra.Api.Application.AI;
using Zayra.Api.Infrastructure.AI;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["ASPNETCORE_URLS"] ?? "http://localhost:5117");

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<SeedAdminOptions>(builder.Configuration.GetSection("SeedAdmin"));

builder.Services.AddControllers();
// CORS locked to an explicit allowlist (SOC: no AllowAnyOrigin). Auth is bearer-token
// via the Authorization header (no cookies), so credentials are not exposed cross-origin.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(options => options.AddPolicy("zayra", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyMethod()
    .AllowAnyHeader()));

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "server=localhost;port=3306;database=zayra;user=root;password=password";
builder.Services.AddDbContext<ZayraDbContext>(options => options
    .UseMySql(connectionString, ServerVersion.Create(new Version(8, 0, 0), Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql))
    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAccessManagementService, AccessManagementService>();
builder.Services.AddScoped<IAttendanceService, AttendanceService>();
builder.Services.AddScoped<IEmployeeManagementService, EmployeeManagementService>();
builder.Services.AddScoped<IOrganizationSetupService, OrganizationSetupService>();
builder.Services.AddScoped<IApprovalWorkflowService, ApprovalWorkflowService>();
builder.Services.AddScoped<IAuthSeeder, AuthSeeder>();
builder.Services.AddScoped<IEmployeeModuleSchemaBootstrapper, EmployeeModuleSchemaBootstrapper>();
builder.Services.AddScoped<IDocumentStorage, LocalDocumentStorage>();
builder.Services.AddScoped<IHijriDateService, HijriDateService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IRecruitmentService, RecruitmentService>();
builder.Services.AddScoped<IPerformanceService, PerformanceService>();
builder.Services.AddScoped<ILeaveService, LeaveService>();
builder.Services.AddScoped<IDataScopeService, DataScopeService>();
builder.Services.AddSingleton(AiOptions.Load(builder.Configuration));
builder.Services.AddScoped<AiRedactionService>();
builder.Services.AddScoped<AiTokenBudgetService>();
builder.Services.AddScoped<IAiGovernanceService, AiGovernanceService>();
builder.Services.AddScoped<IAiPromptBuilder, AiPromptBuilder>();
builder.Services.AddScoped<IAiAuditService, AiAuditService>();
builder.Services.AddScoped<IAiResponseCacheService, AiResponseCacheService>();
builder.Services.AddScoped<IAiAdvisoryService, AiAdvisoryService>();
builder.Services.AddHttpClient<ILlmClient, LlmClient>();
builder.Services.AddHttpContextAccessor();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "KynexOne Workforce API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Security response headers (SOC defence-in-depth: anti-sniffing, anti-clickjacking).
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Permitted-Cross-Domain-Policies"] = "none";
    await next();
});

app.UseCors("zayra");
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

var employeeApi = app.MapGroup("/api/employees").RequireAuthorization();
employeeApi.MapPost("", async (EmployeeCreateRequest request, IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
{
    var created = await service.CreateAsync(RequireTenant(http), request, RequestContext(http), ct);
    return Results.Created($"/api/employees/{created.Employee.Id}", created);
}).RequireAuthorization(policy => policy.RequireRole("Admin", "HR Manager", "HR Officer"));

employeeApi.MapPatch("{id:int}/status", async (int id, EmployeeStatusChangeRequest request, IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
{
    var updated = await service.ChangeStatusAsync(RequireTenant(http), id, request, RequestContext(http), ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
}).RequireAuthorization(policy => policy.RequireRole("Admin", "HR Manager", "HR Officer"));

employeeApi.MapGet("{id:int}/documents", async (int id, IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
    Results.Ok(await service.GetDocumentsAsync(RequireTenant(http), id, ct)));

employeeApi.MapGet("{id:int}/history", async (int id, IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
    Results.Ok(await service.GetHistoryAsync(RequireTenant(http), id, ct)));

employeeApi.MapPost("{id:int}/transfer", async (int id, EmployeeTransferCreateRequest request, IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
{
    var transfer = await service.RequestTransferAsync(RequireTenant(http), id, request, RequestContext(http), ct);
    return transfer is null ? Results.NotFound() : Results.Created($"/api/employees/{id}/transfer/{transfer.Id}", transfer);
}).RequireAuthorization(policy => policy.RequireRole("Admin", "HR Manager", "HR Officer", "Manager"));

employeeApi.MapPost("{id:int}/activate", async (int id, EmployeeStatusChangeRequest request, IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
{
    var updated = await service.ActivateAsync(RequireTenant(http), id, request, RequestContext(http), ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
}).RequireAuthorization(policy => policy.RequireRole("Admin", "HR Manager", "HR Officer"));

employeeApi.MapPost("{id:int}/terminate", async (int id, EmployeeStatusChangeRequest request, IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
{
    var updated = await service.TerminateAsync(RequireTenant(http), id, request, RequestContext(http), ct);
    return updated is null ? Results.NotFound() : Results.Ok(updated);
}).RequireAuthorization(policy => policy.RequireRole("Admin", "HR Manager", "HR Officer"));

employeeApi.MapGet("reports/headcount", async (IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
    Results.Ok(await service.HeadcountAsync(RequireTenant(http), ct)));

employeeApi.MapGet("reports/expiring-documents", async ([FromQuery] int days, IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
    Results.Ok(await service.ExpiringDocumentsAsync(RequireTenant(http), days <= 0 ? 60 : days, ct)));

employeeApi.MapGet("reports/missing-documents", async (IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
    Results.Ok(await service.MissingDocumentsAsync(RequireTenant(http), ct)));

employeeApi.MapGet("reports/status-summary", async (IEmployeeManagementService service, HttpContext http, CancellationToken ct) =>
    Results.Ok(await service.StatusSummaryAsync(RequireTenant(http), ct)));

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("AuthSeeder");
    try
    {
        await scope.ServiceProvider.GetRequiredService<ZayraDbContext>().Database.EnsureCreatedAsync();
        await scope.ServiceProvider.GetRequiredService<IEmployeeModuleSchemaBootstrapper>().EnsureAsync();
        await scope.ServiceProvider.GetRequiredService<IAuthSeeder>().SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Auth seed failed. Verify the MySQL connection string and database permissions.");
    }
}

app.Run();

static Guid RequireTenant(HttpContext http)
{
    var value = http.User.FindFirstValue("tenant_id");
    return Guid.TryParse(value, out var tenantId) ? tenantId : throw new UnauthorizedAccessException("Tenant claim missing.");
}

static RequestContext RequestContext(HttpContext http)
{
    Guid? userId = null;
    var value = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub");
    if (Guid.TryParse(value, out var parsed)) userId = parsed;
    return new RequestContext(http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), userId, RequireTenant(http));
}
