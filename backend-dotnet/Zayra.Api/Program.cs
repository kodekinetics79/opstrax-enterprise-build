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
using Zayra.Api.Infrastructure.Email;
using Zayra.Api.Infrastructure.Documents.Letters;
using Zayra.Api.Infrastructure.Filters;

var builder = WebApplication.CreateBuilder(args);
// Railway injects PORT; fall back to ASPNETCORE_URLS, then local default.
var port = Environment.GetEnvironmentVariable("PORT");
var listenUrl = !string.IsNullOrEmpty(port)
    ? $"http://0.0.0.0:{port}"
    : builder.Configuration["ASPNETCORE_URLS"] ?? "http://0.0.0.0:5117";
builder.WebHost.UseUrls(listenUrl);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<SeedAdminOptions>(builder.Configuration.GetSection("SeedAdmin"));

builder.Services.AddControllers(options =>
{
    options.Filters.Add<SubscriptionGuardFilter>();
});
// CORS: explicit allowlist from config + optional CORS_EXTRA_ORIGINS env var for production deployments
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173" };
var extraOrigins = (builder.Configuration["CORS_EXTRA_ORIGINS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
allowedOrigins = allowedOrigins.Concat(extraOrigins).Distinct().ToArray();
builder.Services.AddCors(options => options.AddPolicy("zayra", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyMethod()
    .AllowAnyHeader()));

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "server=localhost;port=3306;database=zayra;user=root;password=password";
builder.Services.AddDbContext<ZayraDbContext>(options => options
    .UseMySql(connectionString, ServerVersion.Create(new Version(8, 0, 0), Pomelo.EntityFrameworkCore.MySql.Infrastructure.ServerType.MySql),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null))
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
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlatformAdmin", policy => policy.RequireClaim("is_platform_admin", "true"));
});

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
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<ILetterService, LetterService>();
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
builder.Services.AddScoped<IPolicyDocumentService, PolicyDocumentService>();
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

// Global exception handler — must be the outermost middleware.
// Converts unhandled exceptions into structured JSON so clients always get a typed error body
// instead of an empty 500. InvalidOperationException (the service-layer sentinel for bad state)
// maps to 400; everything else is 500 with a traceId for support correlation.
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var ex = feature?.Error;
    var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
    var traceId = ctx.TraceIdentifier;
    log.LogError(ex, "Unhandled exception. TraceId={TraceId} Path={Path}", traceId, ctx.Request.Path);

    ctx.Response.ContentType = "application/json";
    ctx.Response.StatusCode = ex is InvalidOperationException ? 400 : 500;
    await ctx.Response.WriteAsJsonAsync(new
    {
        traceId,
        code = ex is InvalidOperationException ? "bad_request" : "internal_error",
        message = ex is InvalidOperationException
            ? ex.Message
            : "An unexpected error occurred. Quote your traceId when contacting support.",
    });
}));

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

// NOTE: employee endpoints live exclusively in EmployeesController — the former
// minimal-API duplicates here caused AmbiguousMatchException on /api/employees/reports/*.

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
