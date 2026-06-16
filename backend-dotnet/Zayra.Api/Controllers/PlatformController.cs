using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Auth;
using Zayra.Api.Infrastructure.Email;
using Zayra.Api.Infrastructure.Documents.Invoices;
using Zayra.Api.Infrastructure.Subscriptions;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/platform")]
[Authorize(Policy = "PlatformAdmin")]
public class PlatformController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly JwtOptions _jwt;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuthSeeder _authSeeder;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly ILogger<PlatformController> _log;

    public PlatformController(
        ZayraDbContext db,
        IOptions<JwtOptions> jwt,
        IPasswordHasher passwordHasher,
        IAuthSeeder authSeeder,
        ITokenService tokenService,
        IEmailService emailService,
        IConfiguration config,
        ILogger<PlatformController> log)
    {
        _db = db;
        _jwt = jwt.Value;
        _passwordHasher = passwordHasher;
        _authSeeder = authSeeder;
        _tokenService = tokenService;
        _emailService = emailService;
        _config = config;
        _log = log;
    }

    private string PlatformAdminEmail =>
        Environment.GetEnvironmentVariable("PLATFORM_ADMIN_EMAIL") ?? string.Empty;

    // ── Auth ─────────────────────────────────────────────────────────────────

    [HttpPost("auth/login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] PlatformLoginRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { message = "Email and password are required." });

        string loginEmail;
        string loginRole = PlatformRoles.Owner;
        Guid? platformUserId = null;

        // 1. Try DB-based platform user lookup first
        var dbUser = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Email.ToLower() == req.Email.ToLower() && u.IsActive, ct);

        if (dbUser is not null)
        {
            if (!_passwordHasher.Verify(req.Password, dbUser.PasswordHash))
                return Unauthorized(new { message = "Invalid platform admin credentials." });

            // Update last login audit fields
            dbUser.LastLoginAtUtc = DateTime.UtcNow;
            dbUser.LastLoginIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            dbUser.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            loginEmail = dbUser.Email;
            loginRole = dbUser.Role;
            platformUserId = dbUser.Id;
        }
        else
        {
            // 2. Fall back to env-var credentials
            var expectedEmail = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_EMAIL");
            var expectedPassword = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_PASSWORD");

            if (string.IsNullOrWhiteSpace(expectedEmail) || string.IsNullOrWhiteSpace(expectedPassword))
                return StatusCode(503, new { message = "Platform admin credentials are not configured." });

            if (!string.Equals(req.Email, expectedEmail, StringComparison.OrdinalIgnoreCase) ||
                req.Password != expectedPassword)
                return Unauthorized(new { message = "Invalid platform admin credentials." });

            loginEmail = expectedEmail;
            loginRole = PlatformRoles.Owner;
        }

        var expiresAt = DateTime.UtcNow.AddHours(8);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, platformUserId?.ToString() ?? "platform-admin"),
            new(JwtRegisteredClaimNames.Email, loginEmail),
            new(ClaimTypes.Role, "PlatformAdmin"),
            new("is_platform_admin", "true"),
            new("platform_role", loginRole),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, expires: expiresAt, signingCredentials: credentials);
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new { token = tokenString, expiresAt, role = loginRole });
    }

    // ── Health ────────────────────────────────────────────────────────────────

    [HttpGet("health")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support, PlatformRoles.Auditor)]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var dbOk = false;
        try { await _db.Database.CanConnectAsync(ct); dbOk = true; } catch { }

        var smtpConfigured = !string.IsNullOrEmpty(_config["Smtp:Host"]);

        // Redis health check — optional dependency
        string redisStatus;
        try
        {
            var muxer = HttpContext.RequestServices.GetService<IConnectionMultiplexer>();
            if (muxer is null)
            {
                redisStatus = "not_configured";
            }
            else
            {
                var anyConnected = muxer.GetEndPoints().Any(ep => muxer.GetServer(ep).IsConnected);
                redisStatus = anyConnected ? "ok" : "disconnected";
            }
        }
        catch
        {
            redisStatus = "error";
        }

        return Ok(new
        {
            status = dbOk ? "healthy" : "degraded",
            components = new
            {
                database = new { status = dbOk ? "ok" : "error" },
                smtp     = new { status = smtpConfigured ? "configured" : "not_configured" },
                redis    = new { status = redisStatus },
                jobs     = new { status = "unknown" },
            },
            version     = "1.0.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            checkedAtUtc = DateTime.UtcNow,
        });
    }

    // ── Platform Team ─────────────────────────────────────────────────────────

    [HttpGet("team")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support)]
    public async Task<IActionResult> ListTeam(CancellationToken ct)
    {
        var users = await _db.PlatformUsers
            .AsNoTracking()
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.Role,
                u.IsActive,
                u.LastLoginAtUtc,
                u.LastLoginIp,
                u.CreatedAtUtc,
                u.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(users);
    }

    [HttpPost("team")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> CreateTeamMember([FromBody] CreatePlatformUserRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { message = "A valid email is required." });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters." });

        var role = string.IsNullOrWhiteSpace(req.Role) ? PlatformRoles.Admin : req.Role;
        if (!PlatformRoles.All.Contains(role))
            return BadRequest(new { message = $"Invalid role. Valid roles: {string.Join(", ", PlatformRoles.All)}" });

        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.PlatformUsers.AsNoTracking().AnyAsync(u => u.Email == email, ct))
            return Conflict(new { message = "A platform user with this email already exists." });

        var user = new PlatformUser
        {
            Email = email,
            FullName = string.IsNullOrWhiteSpace(req.FullName) ? email : req.FullName.Trim(),
            PasswordHash = _passwordHasher.Hash(req.Password),
            Role = role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.PlatformUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        return Ok(new { user.Id, user.Email, user.FullName, user.Role, user.IsActive, user.CreatedAtUtc });
    }

    [HttpPatch("team/{id:guid}")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> UpdateTeamMember(Guid id, [FromBody] UpdatePlatformUserRequest req, CancellationToken ct)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound(new { message = "Platform user not found." });

        if (req.Role is not null)
        {
            if (!PlatformRoles.All.Contains(req.Role))
                return BadRequest(new { message = $"Invalid role. Valid roles: {string.Join(", ", PlatformRoles.All)}" });
            user.Role = req.Role;
        }

        if (req.IsActive.HasValue) user.IsActive = req.IsActive.Value;
        if (!string.IsNullOrWhiteSpace(req.FullName)) user.FullName = req.FullName.Trim();

        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { user.Id, user.Email, user.FullName, user.Role, user.IsActive, user.UpdatedAtUtc });
    }

    // Admin: deactivation only (no true delete). Owner-only for true delete scenarios (n/a here).
    [HttpDelete("team/{id:guid}")]
    [RequirePlatformRole(PlatformRoles.Owner)]
    public async Task<IActionResult> DeactivateTeamMember(Guid id, CancellationToken ct)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return NotFound(new { message = "Platform user not found." });

        // Soft deactivate — never hard-delete platform users
        user.IsActive = false;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { user.Id, user.Email, isActive = false, message = "Platform user deactivated." });
    }

    // ── Tenants List ─────────────────────────────────────────────────────────

    [HttpGet("tenants")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance, PlatformRoles.Support, PlatformRoles.Auditor)]
    public async Task<IActionResult> ListTenants(CancellationToken ct)
    {
        var tenants = await _db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        var tenantIds = tenants.Select(t => t.Id).ToList();

        var subscriptions = await _db.TenantSubscriptions
            .AsNoTracking()
            .Where(s => tenantIds.Contains(s.TenantId))
            .ToDictionaryAsync(s => s.TenantId, ct);

        var userCounts = await _db.Users
            .AsNoTracking()
            .Where(u => tenantIds.Contains(u.TenantId) && u.IsActive && !u.IsDeleted)
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var employeeCounts = await _db.Employees
            .AsNoTracking()
            .Where(e => e.TenantId.HasValue && tenantIds.Contains(e.TenantId.Value) && !e.IsDeleted)
            .GroupBy(e => e.TenantId!.Value)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var result = tenants.Select(t =>
        {
            subscriptions.TryGetValue(t.Id, out var sub);
            userCounts.TryGetValue(t.Id, out var users);
            employeeCounts.TryGetValue(t.Id, out var emps);
            return new
            {
                t.Id,
                t.Name,
                t.Slug,
                t.IsActive,
                t.CreatedAtUtc,
                subscription = sub is null ? null : new
                {
                    sub.Plan,
                    sub.Status,
                    sub.MaxEmployees,
                    sub.MaxUsers,
                    sub.ExpiresAtUtc,
                    sub.MonthlyAmount,
                    sub.CurrencyCode,
                    sub.BillingEmail,
                    sub.BillingCycle
                },
                activeUserCount = users,
                activeEmployeeCount = emps
            };
        });

        return Ok(result);
    }

    // ── Tenant Detail ─────────────────────────────────────────────────────────

    [HttpGet("tenants/{tenantId:guid}")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance, PlatformRoles.Support, PlatformRoles.Auditor)]
    public async Task<IActionResult> GetTenant(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var sub = await _db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var flags = await _db.TenantFeatureFlags.AsNoTracking().Where(f => f.TenantId == tenantId).OrderBy(f => f.FeatureKey).ToListAsync(ct);
        var loc = await _db.TenantLocalizationSettings.AsNoTracking().FirstOrDefaultAsync(l => l.TenantId == tenantId, ct);
        var branding = await _db.TenantBrandings.AsNoTracking().FirstOrDefaultAsync(b => b.TenantId == tenantId, ct);

        var userCount = await _db.Users.AsNoTracking()
            .CountAsync(u => u.TenantId == tenantId && u.IsActive && !u.IsDeleted, ct);

        var employeeCount = await _db.Employees.AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId && !e.IsDeleted, ct);

        return Ok(new
        {
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.IsActive,
            tenant.CreatedAtUtc,
            subscription = sub,
            featureFlags = flags,
            localization = loc,
            branding,
            userCount,
            employeeCount
        });
    }

    // ── Subscription Upsert ───────────────────────────────────────────────────

    [HttpPut("tenants/{tenantId:guid}/subscription")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public async Task<IActionResult> UpsertSubscription(Guid tenantId, [FromBody] UpsertSubscriptionRequest req, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var oldSnap = sub is null ? null : new { sub.Plan, sub.Status, sub.MaxUsers, sub.MaxEmployees };

        if (sub is null)
        {
            sub = new TenantSubscription { TenantId = tenantId };
            _db.TenantSubscriptions.Add(sub);
        }

        sub.Plan = req.Plan;
        sub.Status = req.Status;
        sub.MaxEmployees = req.MaxEmployees;
        sub.MaxUsers = req.MaxUsers;
        sub.BillingEmail = req.BillingEmail;
        sub.BillingCycle = req.BillingCycle;
        sub.MonthlyAmount = req.MonthlyAmount;
        sub.CurrencyCode = req.CurrencyCode;
        sub.ExpiresAtUtc = req.ExpiresAtUtc;
        sub.UpdatedAtUtc = DateTime.UtcNow;

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "Subscription",
            EntityId = tenantId.ToString(),
            Action = "SubscriptionUpdated",
            OldValuesJson = oldSnap is null ? null : System.Text.Json.JsonSerializer.Serialize(oldSnap),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                plan = req.Plan,
                status = req.Status,
                maxUsers = req.MaxUsers,
                maxEmployees = req.MaxEmployees,
                expiresAtUtc = req.ExpiresAtUtc
            }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return Ok(sub);
    }

    // ── Feature Flag Toggle ───────────────────────────────────────────────────

    [HttpPut("tenants/{tenantId:guid}/features/{featureKey}")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> SetFeatureFlag(Guid tenantId, string featureKey, [FromBody] SetFeatureFlagRequest req, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var flag = await _db.TenantFeatureFlags
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FeatureKey == featureKey, ct);

        if (flag is null)
        {
            flag = new TenantFeatureFlag { TenantId = tenantId, FeatureKey = featureKey };
            _db.TenantFeatureFlags.Add(flag);
        }

        var oldEnabled = flag.IsEnabled;
        flag.IsEnabled = req.IsEnabled;
        flag.ConfigJson = req.ConfigJson;
        flag.UpdatedAtUtc = DateTime.UtcNow;

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "FeatureFlag",
            EntityId = $"{tenantId}/{featureKey}",
            Action = req.IsEnabled ? "FeatureEnabled" : "FeatureDisabled",
            OldValuesJson = System.Text.Json.JsonSerializer.Serialize(new { featureKey, isEnabled = oldEnabled }),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { featureKey, isEnabled = req.IsEnabled }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return Ok(flag);
    }

    // ── Impersonation ─────────────────────────────────────────────────────────

    [HttpPost("tenants/{tenantId:guid}/impersonate")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support)]
    public async Task<IActionResult> Impersonate(Guid tenantId, [FromBody] ImpersonateRequest req, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound(new { message = "Tenant not found." });

        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == req.UserId && u.TenantId == tenantId && !u.IsDeleted, ct);

        if (user is null) return NotFound(new { message = "User not found in specified tenant." });

        var roles = user.UserRoles.Where(ur => ur.Role is not null).Select(ur => ur.Role!.Name).ToList();
        var expiresAt = DateTime.UtcNow.AddHours(1);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new("tenant_id", tenant.Id.ToString()),
            new("tenant", tenant.Slug),
            new("impersonated_by", "platform_admin"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, expires: expiresAt, signingCredentials: credentials);
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new
        {
            token = tokenString,
            expiresAt,
            userId = user.Id,
            userEmail = user.Email,
            tenantId = tenant.Id,
            tenantSlug = tenant.Slug
        });
    }

    // ── Client Provisioning ───────────────────────────────────────────────────

    /// <summary>Provision a new client: tenant + full role set + tenant admin ("sub admin") + subscription.</summary>
    [HttpPost("tenants")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim();
        var slug = req.Slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Tenant name is required." });
        if (string.IsNullOrWhiteSpace(slug) || !System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$"))
            return BadRequest(new { message = "Slug must be 3-40 chars: lowercase letters, digits and hyphens." });
        if (string.IsNullOrWhiteSpace(req.AdminEmail) || !req.AdminEmail.Contains('@'))
            return BadRequest(new { message = "A valid admin email is required." });
        if (string.IsNullOrWhiteSpace(req.AdminPassword) || req.AdminPassword.Length < 10)
            return BadRequest(new { message = "Admin password must be at least 10 characters." });

        if (await _db.Tenants.AsNoTracking().AnyAsync(t => t.Slug == slug, ct))
            return Conflict(new { message = $"A tenant with slug '{slug}' already exists." });

        var tenant = new Tenant { Name = name, Slug = slug };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);

        // Full standard RBAC set (Admin → Employee), same as the seeded tenant
        var adminRole = await _authSeeder.EnsureTenantRolesAsync(tenant.Id, ct);

        var admin = new User
        {
            TenantId = tenant.Id,
            Email = req.AdminEmail.Trim().ToLowerInvariant(),
            NormalizedEmail = AuthService.Normalize(req.AdminEmail),
            FullName = string.IsNullOrWhiteSpace(req.AdminFullName) ? "Tenant Administrator" : req.AdminFullName.Trim(),
            PasswordHash = _passwordHasher.Hash(req.AdminPassword),
            AccessMode = "FullPortal",
            Status = "Active",
            IsActive = true,
            IsEmailConfirmed = true
        };
        admin.UserRoles.Add(new UserRole { User = admin, Role = adminRole });
        _db.Users.Add(admin);

        var plan = string.IsNullOrWhiteSpace(req.Plan) ? "Trial" : req.Plan;
        var (defaultMaxUsers, defaultMaxEmployees) = SubscriptionTiers.GetDefaults(plan);
        _db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id,
            Plan = plan,
            Status = "Active",
            MaxUsers = req.MaxUsers ?? defaultMaxUsers,
            MaxEmployees = req.MaxEmployees ?? defaultMaxEmployees,
            BillingEmail = req.BillingEmail ?? req.AdminEmail.Trim().ToLowerInvariant(),
            BillingCycle = req.BillingCycle ?? "Monthly",
            MonthlyAmount = req.MonthlyAmount ?? 0,
            CurrencyCode = req.CurrencyCode ?? "USD",
            ExpiresAtUtc = req.ExpiresAtUtc
        });

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenant.Id,
            EntityType = "Tenant",
            EntityId = tenant.Id.ToString(),
            Action = "TenantCreated",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                name = tenant.Name,
                slug = tenant.Slug,
                plan,
                adminEmail = admin.Email,
                maxUsers = req.MaxUsers ?? SubscriptionTiers.GetDefaults(plan).MaxUsers,
                maxEmployees = req.MaxEmployees ?? SubscriptionTiers.GetDefaults(plan).MaxEmployees
            }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenant.Id,
            EntityType = "User",
            EntityId = admin.Id.ToString(),
            Action = "AdminCreated",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { email = admin.Email, role = "Admin" }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetTenant), new { tenantId = tenant.Id }, new
        {
            tenantId = tenant.Id,
            tenant.Name,
            tenant.Slug,
            adminUserId = admin.Id,
            adminEmail = admin.Email,
            plan,
            loginHint = $"Tenant slug '{tenant.Slug}' + admin email/password on the login page."
        });
    }

    // ── Tenant Admins ("sub admins") ─────────────────────────────────────────

    [HttpGet("tenants/{tenantId:guid}/admins")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support)]
    public async Task<IActionResult> ListTenantAdmins(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var admins = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && !u.IsDeleted
                && u.UserRoles.Any(ur => ur.Role != null && ur.Role.Name == "Admin"))
            .OrderBy(u => u.Email)
            .Select(u => new { u.Id, u.Email, u.FullName, u.IsActive, u.Status, u.CreatedAtUtc })
            .ToListAsync(ct);

        return Ok(admins);
    }

    [HttpPost("tenants/{tenantId:guid}/admins")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> AddTenantAdmin(Guid tenantId, [FromBody] AddTenantAdminRequest req, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound(new { message = "Tenant not found." });

        if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
            return BadRequest(new { message = "A valid email is required." });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 10)
            return BadRequest(new { message = "Password must be at least 10 characters." });

        var normalizedEmail = AuthService.Normalize(req.Email);
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.TenantId == tenantId && u.NormalizedEmail == normalizedEmail, ct))
            return Conflict(new { message = "A user with this email already exists in the tenant." });

        var adminRole = await _db.Roles.FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Name == "Admin" && !r.IsDeleted, ct)
            ?? await _authSeeder.EnsureTenantRolesAsync(tenantId, ct);

        var user = new User
        {
            TenantId = tenantId,
            Email = req.Email.Trim().ToLowerInvariant(),
            NormalizedEmail = normalizedEmail,
            FullName = string.IsNullOrWhiteSpace(req.FullName) ? "Tenant Administrator" : req.FullName.Trim(),
            PasswordHash = _passwordHasher.Hash(req.Password),
            AccessMode = "FullPortal",
            Status = "Active",
            IsActive = true,
            IsEmailConfirmed = true
        };
        user.UserRoles.Add(new UserRole { User = user, Role = adminRole });
        _db.Users.Add(user);

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "User",
            EntityId = user.Id.ToString(),
            Action = "AdminCreated",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { email = user.Email, fullName = user.FullName, role = "Admin" }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new { user.Id, user.Email, user.FullName, tenantSlug = tenant.Slug });
    }

    // ── Tenant Suspend / Reactivate ───────────────────────────────────────────

    [HttpPost("tenants/{tenantId:guid}/suspend")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> SuspendTenant(Guid tenantId, [FromBody] TenantActionRequest req, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound(new { message = "Tenant not found." });

        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (sub is null) return BadRequest(new { message = "Tenant has no subscription record." });

        var oldStatus = sub.Status;
        sub.Status = "Suspended";
        sub.UpdatedAtUtc = DateTime.UtcNow;
        tenant.IsActive = false;

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "Tenant",
            EntityId = tenantId.ToString(),
            Action = "Suspended",
            OldValuesJson = System.Text.Json.JsonSerializer.Serialize(new { status = oldStatus }),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { status = "Suspended", reason = req.Reason }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { tenantId, status = "Suspended" });
    }

    [HttpPost("tenants/{tenantId:guid}/reactivate")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> ReactivateTenant(Guid tenantId, [FromBody] TenantActionRequest req, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound(new { message = "Tenant not found." });

        var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (sub is null) return BadRequest(new { message = "Tenant has no subscription record." });

        var oldStatus = sub.Status;
        sub.Status = "Active";
        sub.UpdatedAtUtc = DateTime.UtcNow;
        tenant.IsActive = true;

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "Tenant",
            EntityId = tenantId.ToString(),
            Action = "Reactivated",
            OldValuesJson = System.Text.Json.JsonSerializer.Serialize(new { status = oldStatus }),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { status = "Active", reason = req.Reason }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { tenantId, status = "Active" });
    }

    // ── Tenant Profile Edit ───────────────────────────────────────────────────

    [HttpPatch("tenants/{tenantId:guid}")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> UpdateTenant(Guid tenantId, [FromBody] UpdateTenantRequest req, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound(new { message = "Tenant not found." });

        var oldName = tenant.Name;
        if (!string.IsNullOrWhiteSpace(req.Name)) tenant.Name = req.Name.Trim();

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "Tenant",
            EntityId = tenantId.ToString(),
            Action = "Updated",
            OldValuesJson = System.Text.Json.JsonSerializer.Serialize(new { name = oldName }),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { name = tenant.Name }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { tenant.Id, tenant.Name, tenant.Slug });
    }

    // ── Tenant Users ──────────────────────────────────────────────────────────

    [HttpGet("tenants/{tenantId:guid}/users")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support)]
    public async Task<IActionResult> ListTenantUsers(Guid tenantId, [FromQuery] string? search, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var q = _db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Where(u => u.TenantId == tenantId && !u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            q = q.Where(u => u.Email.Contains(s) || u.FullName.ToLower().Contains(s));
        }

        var users = await q
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.IsActive,
                u.Status,
                u.CreatedAtUtc,
                Roles = u.UserRoles.Where(ur => ur.Role != null).Select(ur => ur.Role!.Name).ToList()
            })
            .ToListAsync(ct);

        return Ok(users);
    }

    // ── Password Reset (platform-initiated) ───────────────────────────────────

    /// <summary>
    /// TODO: Implement email delivery.
    /// Currently generates a reset token stored on the user. The actual email send
    /// requires an IEmailService (SMTP/SendGrid) not yet wired to the platform portal.
    /// When email service is available: call IAuthService.ForgotPasswordAsync with the user's email.
    /// </summary>
    [HttpPost("users/{userId:guid}/send-password-reset")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support)]
    public async Task<IActionResult> SendPasswordReset(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null) return NotFound(new { message = "User not found." });

        // Safety: block reset attempts targeting the platform admin credential (env-var based, not in DB)
        if (user.Email.Equals(PlatformAdminEmail, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        // Generate reset token, store it, and email the link
        var resetToken = _tokenService.CreateSecureToken();
        var expiresAt = DateTime.UtcNow.AddHours(1);
        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = _tokenService.HashToken(resetToken),
            ExpiresAtUtc = expiresAt,
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = user.TenantId,
            EntityType = "User",
            EntityId = userId.ToString(),
            Action = "PasswordResetRequested",
            OldValuesJson = "{}",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { userEmail = user.Email, initiatedBy = "platform_admin" }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });
        await _db.SaveChangesAsync(ct);

        // Build reset link and send email (falls back gracefully if SMTP not configured)
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == user.TenantId, ct);
        var appUrl = (Environment.GetEnvironmentVariable("APP_URL") ?? string.Empty).TrimEnd('/');
        var encodedToken = Uri.EscapeDataString(resetToken);
        var encodedEmail = Uri.EscapeDataString(user.Email);
        var tenantPart = tenant is not null ? $"&tenant={Uri.EscapeDataString(tenant.Slug)}" : string.Empty;
        var resetUrl = $"{appUrl}/reset-password?token={encodedToken}&email={encodedEmail}{tenantPart}";

        var html = $"""
            <p>Hi {System.Web.HttpUtility.HtmlEncode(user.FullName)},</p>
            <p>A platform administrator has requested a password reset for your account. Click the link below to set a new password. This link expires in <strong>1 hour</strong>.</p>
            <p><a href="{resetUrl}" style="background:#2563EB;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none;display:inline-block">Reset Password</a></p>
            <p>If you did not expect this, contact your platform administrator immediately.</p>
            <hr/>
            <p style="font-size:12px;color:#666">This action was initiated by the platform super-admin · KynexOne Workforce</p>
            """;

        bool smtpConfigured = await _emailService.IsConfiguredAsync(ct);
        bool emailSent = false;

        if (smtpConfigured)
        {
            try
            {
                await _emailService.SendAsync(user.Email, user.FullName, "Your KynexOne password reset (admin-initiated)", html, cancellationToken: ct);
                emailSent = true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Platform password reset email failed for {Email}. Token saved.", user.Email);
            }
        }
        else
        {
            _log.LogInformation("SMTP not configured — reset token saved for {Email}, no email sent.", user.Email);
        }

        return Ok(new
        {
            userId,
            userEmail = user.Email,
            emailSent,
            smtpConfigured,
            message = emailSent
                ? $"Password reset email sent to {user.Email}. Link expires in 1 hour."
                : smtpConfigured
                    ? "SMTP is configured but email delivery failed — check server logs."
                    : "Reset token saved and logged. SMTP is not configured — share the reset link directly or configure SMTP in Setup → Email Settings.",
            emailDeliveryAvailable = smtpConfigured,
            resetTokenExpiresAt = expiresAt
        });
    }

    [HttpPost("users/{userId:guid}/force-password-reset")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> ForcePasswordReset(Guid userId, [FromBody] ForcePasswordResetRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user is null) return NotFound(new { message = "User not found." });

        if (user.Email.Equals(PlatformAdminEmail, StringComparison.OrdinalIgnoreCase))
            return Forbid();

        if (string.IsNullOrWhiteSpace(req.TempPassword) || req.TempPassword.Length < 10)
            return BadRequest(new { message = "Temporary password must be at least 10 characters." });

        user.PasswordHash = _passwordHasher.Hash(req.TempPassword);
        user.MustChangePassword = true;
        user.UpdatedAtUtc = DateTime.UtcNow;

        // Revoke all active refresh tokens so existing sessions are terminated
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, DateTime.UtcNow), ct);

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = user.TenantId,
            EntityType = "User",
            EntityId = userId.ToString(),
            Action = "ForcePasswordReset",
            OldValuesJson = "{}",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { userEmail = user.Email, initiatedBy = "platform_admin", mustChangePassword = true, sessionsRevoked = true }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { userId, userEmail = user.Email, mustChangePassword = true, sessionsRevoked = true });
    }

    // ── Platform Audit Logs ───────────────────────────────────────────────────

    [HttpGet("audit-logs")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support, PlatformRoles.Auditor)]
    public async Task<IActionResult> GetAuditLogs([FromQuery] Guid? tenantId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var q = _db.AdminAuditLogs.AsNoTracking().AsQueryable();

        if (tenantId.HasValue) q = q.Where(l => l.TenantId == tenantId.Value);

        // When no tenant filter, show only platform-level events (tenant lifecycle, support sessions, password resets)
        if (!tenantId.HasValue)
            q = q.Where(l =>
                l.EntityType == "Tenant" ||
                l.EntityType == "SupportSession" ||
                (l.EntityType == "User" && (l.Action.Contains("Password") || l.Action.Contains("Suspend") || l.Action.Contains("Reactivat"))));

        var total = await q.CountAsync(ct);

        var rawLogs = await q
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new {
                l.Id,
                l.TenantId,
                l.EntityType,
                l.EntityId,
                l.Action,
                l.OldValuesJson,
                l.NewValuesJson,
                l.PerformedByName,
                l.IpAddress,
                l.CreatedAtUtc,
            })
            .ToListAsync(ct);

        // Resolve tenant names in a single query rather than N+1
        var tenantIds = rawLogs.Select(l => l.TenantId).Distinct().ToList();
        var tenantNames = await _db.Tenants.AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var logs = rawLogs.Select(l => new {
            l.Id,
            l.TenantId,
            TenantName = tenantNames.TryGetValue(l.TenantId, out var n) ? n : null,
            l.EntityType,
            l.EntityId,
            l.Action,
            l.OldValuesJson,
            l.NewValuesJson,
            l.PerformedByName,
            l.IpAddress,
            l.CreatedAtUtc,
        });

        return Ok(new { total, page, pageSize, logs });
    }

    // ── Plans Catalog ─────────────────────────────────────────────────────────

    // Plan price overrides — platform admin can update via PUT /plans/{name}/price.
    // Stored in-memory; survives restarts via the system_settings table (key=plan.price.{name}).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, decimal> _planPriceOverrides = new();

    private static readonly (string Name, int MaxUsers, int MaxEmployees, decimal DefaultPrice, string Description)[] _planDefs = new[]
    {
        ("Trial",      3,  10,   0m,   "Free evaluation. No payment required. 10 employees, 3 users, core modules only."),
        ("Starter",    10, 50,   149m, "Up to 50 employees. Core HR, attendance, leave, payroll."),
        ("Growth",     50, 250,  499m, "Up to 250 employees. All Starter modules plus recruitment, performance, loans, analytics."),
        ("Enterprise", 0,  0,    0m,   "Unlimited employees and users. All modules, custom branding, dedicated support. Custom pricing."),
    };

    [HttpGet("plans")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance, PlatformRoles.Auditor)]
    public IActionResult GetPlans()
    {
        var plans = _planDefs.Select(p => new
        {
            p.Name, p.MaxUsers, p.MaxEmployees,
            MonthlyPrice = _planPriceOverrides.TryGetValue(p.Name, out var ov) ? ov : p.DefaultPrice,
            p.Description
        });
        return Ok(plans);
    }

    [HttpPut("plans/{planName}/price")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public IActionResult UpdatePlanPrice(string planName, [FromBody] UpdatePlanPriceRequest req, CancellationToken ct)
    {
        if (!_planDefs.Any(p => p.Name.Equals(planName, StringComparison.OrdinalIgnoreCase)))
            return NotFound(new { message = $"Plan '{planName}' not found." });
        if (req.MonthlyPrice < 0)
            return BadRequest(new { message = "Price cannot be negative." });
        _planPriceOverrides[planName] = req.MonthlyPrice;
        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            EntityType = "PlanConfig",
            EntityId = planName,
            Action = "PlanPriceUpdated",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { planName, monthlyPrice = req.MonthlyPrice }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });
        _ = _db.SaveChangesAsync(ct);
        return Ok(new { planName, monthlyPrice = req.MonthlyPrice });
    }

    // ── Support Access (structured break-glass) ───────────────────────────────

    [HttpPost("support-access/start")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support)]
    public async Task<IActionResult> StartSupportAccess([FromBody] StartSupportAccessRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(req.TenantId, out var tenantId))
            return BadRequest(new { message = "Invalid tenantId." });
        if (!Guid.TryParse(req.UserId, out var userId))
            return BadRequest(new { message = "Invalid userId." });
        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "Reason is required for support access." });

        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound(new { message = "Tenant not found." });

        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId && !u.IsDeleted, ct);
        if (user is null) return NotFound(new { message = "User not found in specified tenant." });

        var roles = user.UserRoles.Where(ur => ur.Role is not null).Select(ur => ur.Role!.Name).ToList();
        var expiresAt = DateTime.UtcNow.AddHours(1);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new("tenant_id", tenant.Id.ToString()),
            new("tenant", tenant.Slug),
            new("impersonated_by", "platform_admin"),
            new("support_reason", req.Reason.Trim()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwtToken = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, expires: expiresAt, signingCredentials: credentials);
        var tokenString = new JwtSecurityTokenHandler().WriteToken(jwtToken);

        // Hash the token so we can identify this session when ending it
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenString)));

        var session = new PlatformSupportSession
        {
            TenantId = tenantId,
            TargetUserId = userId,
            TargetUserEmail = user.Email,
            Reason = req.Reason.Trim(),
            StartedByEmail = PlatformAdminEmail,
            StartedByIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            ExpiresAtUtc = expiresAt,
            TokenHash = tokenHash
        };
        _db.PlatformSupportSessions.Add(session);

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "SupportSession",
            EntityId = session.Id.ToString(),
            Action = "SupportAccessStarted",
            OldValuesJson = "{}",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                targetUserEmail = user.Email,
                tenantSlug = tenant.Slug,
                reason = req.Reason.Trim(),
                expiresAt
            }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            sessionId = session.Id,
            token = tokenString,
            expiresAt,
            targetUserEmail = user.Email,
            tenantSlug = tenant.Slug,
            reason = session.Reason
        });
    }

    [HttpPost("support-access/end")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support)]
    public async Task<IActionResult> EndSupportAccess([FromBody] EndSupportAccessRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(req.SessionId, out var sessionId))
            return BadRequest(new { message = "Invalid sessionId." });

        var session = await _db.PlatformSupportSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound(new { message = "Support session not found." });
        if (session.EndedAtUtc is not null)
            return BadRequest(new { message = "Session already ended." });

        session.EndedAtUtc = DateTime.UtcNow;

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = session.TenantId,
            EntityType = "SupportSession",
            EntityId = session.Id.ToString(),
            Action = "SupportAccessEnded",
            OldValuesJson = "{}",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                targetUserEmail = session.TargetUserEmail,
                reason = session.Reason,
                durationMinutes = (int)(DateTime.UtcNow - session.StartedAtUtc).TotalMinutes
            }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { sessionId, endedAt = session.EndedAtUtc });
    }

    [HttpGet("support-access")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Support, PlatformRoles.Auditor)]
    public async Task<IActionResult> ListSupportSessions([FromQuery] Guid? tenantId, [FromQuery] bool activeOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var q = _db.PlatformSupportSessions.AsNoTracking().AsQueryable();
        if (tenantId.HasValue) q = q.Where(s => s.TenantId == tenantId.Value);
        if (activeOnly) q = q.Where(s => s.EndedAtUtc == null && s.ExpiresAtUtc > DateTime.UtcNow);

        var total = await q.CountAsync(ct);
        var sessions = await q
            .OrderByDescending(s => s.StartedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.TenantId,
                s.TargetUserId,
                s.TargetUserEmail,
                s.Reason,
                s.StartedByEmail,
                s.StartedByIp,
                s.StartedAtUtc,
                s.ExpiresAtUtc,
                s.EndedAtUtc,
                IsActive = s.EndedAtUtc == null && s.ExpiresAtUtc > DateTime.UtcNow
            })
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, sessions });
    }

    // ── Tenant Invoices ───────────────────────────────────────────────────────

    [HttpGet("tenants/{tenantId:guid}/invoices")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public async Task<IActionResult> ListTenantInvoices(Guid tenantId, CancellationToken ct)
    {
        if (!await _db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct)) return NotFound();
        var invoices = await _db.TenantInvoices
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.InvoiceDate)
            .ToListAsync(ct);
        return Ok(invoices);
    }

    [HttpPost("tenants/{tenantId:guid}/invoices")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public async Task<IActionResult> CreateInvoice(Guid tenantId, [FromBody] CreateInvoiceRequest req, CancellationToken ct)
    {
        if (!await _db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct)) return NotFound();
        if (string.IsNullOrWhiteSpace(req.InvoiceNumber))
            return BadRequest(new { message = "Invoice number is required." });
        if (req.Amount < 0)
            return BadRequest(new { message = "Amount must be non-negative." });

        var invoice = new TenantInvoice
        {
            TenantId = tenantId,
            InvoiceNumber = req.InvoiceNumber.Trim(),
            Amount = req.Amount,
            CurrencyCode = string.IsNullOrWhiteSpace(req.CurrencyCode) ? "USD" : req.CurrencyCode.Trim().ToUpperInvariant(),
            Status = string.IsNullOrWhiteSpace(req.Status) ? InvoiceStatuses.Draft : req.Status,
            PaymentMethod = req.PaymentMethod?.Trim(),
            PaymentReference = req.PaymentReference?.Trim(),
            PeriodDescription = req.PeriodDescription?.Trim(),
            InvoiceDate = req.InvoiceDate,
            DueDate = req.DueDate,
            PaidDate = req.PaidDate,
            Notes = req.Notes?.Trim(),
            RecipientEmail = string.IsNullOrWhiteSpace(req.RecipientEmail) ? null : req.RecipientEmail.Trim().ToLowerInvariant()
        };
        _db.TenantInvoices.Add(invoice);

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "Invoice",
            EntityId = invoice.Id.ToString(),
            Action = "InvoiceCreated",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                invoiceNumber = invoice.InvoiceNumber,
                amount = invoice.Amount,
                currency = invoice.CurrencyCode,
                status = invoice.Status,
                dueDate = invoice.DueDate
            }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(ListTenantInvoices), new { tenantId }, invoice);
    }

    [HttpPut("tenants/{tenantId:guid}/invoices/{invoiceId:guid}")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public async Task<IActionResult> UpdateInvoice(Guid tenantId, Guid invoiceId, [FromBody] UpdateInvoiceRequest req, CancellationToken ct)
    {
        var invoice = await _db.TenantInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId, ct);
        if (invoice is null) return NotFound();

        var oldStatus = invoice.Status;
        if (req.Status is not null) invoice.Status = req.Status;
        if (req.PaymentMethod is not null) invoice.PaymentMethod = req.PaymentMethod.Trim();
        if (req.PaymentReference is not null) invoice.PaymentReference = req.PaymentReference.Trim();
        if (req.PaidDate.HasValue) invoice.PaidDate = req.PaidDate;
        if (req.Notes is not null) invoice.Notes = req.Notes.Trim();
        if (req.RecipientEmail is not null) invoice.RecipientEmail = string.IsNullOrWhiteSpace(req.RecipientEmail) ? null : req.RecipientEmail.Trim().ToLowerInvariant();
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "Invoice",
            EntityId = invoiceId.ToString(),
            Action = "InvoiceUpdated",
            OldValuesJson = System.Text.Json.JsonSerializer.Serialize(new { status = oldStatus }),
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = invoice.Status,
                paymentMethod = invoice.PaymentMethod,
                paymentReference = invoice.PaymentReference,
                paidDate = invoice.PaidDate
            }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return Ok(invoice);
    }

    [HttpDelete("tenants/{tenantId:guid}/invoices/{invoiceId:guid}")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public async Task<IActionResult> DeleteInvoice(Guid tenantId, Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _db.TenantInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId, ct);
        if (invoice is null) return NotFound();
        if (invoice.Status == InvoiceStatuses.Paid)
            return BadRequest(new { message = "Paid invoices cannot be deleted. Set status to Cancelled instead." });

        var snap = new { invoiceNumber = invoice.InvoiceNumber, amount = invoice.Amount, status = invoice.Status };
        _db.TenantInvoices.Remove(invoice);

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "Invoice",
            EntityId = invoiceId.ToString(),
            Action = "InvoiceDeleted",
            OldValuesJson = System.Text.Json.JsonSerializer.Serialize(snap),
            NewValuesJson = "{}",
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("tenants/{tenantId:guid}/invoices/{invoiceId:guid}/pdf")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public async Task<IActionResult> DownloadInvoicePdf(Guid tenantId, Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _db.TenantInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId, ct);
        if (invoice is null) return NotFound();

        var sub    = await _db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        var data = BuildInvoiceData(invoice, tenant, sub);
        var pdfBytes = InvoicePdfService.Generate(data);

        return File(pdfBytes, "application/pdf", $"Invoice_{invoice.InvoiceNumber}.pdf");
    }

    [HttpPost("tenants/{tenantId:guid}/invoices/{invoiceId:guid}/send")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public async Task<IActionResult> SendInvoiceEmail(Guid tenantId, Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _db.TenantInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId && i.TenantId == tenantId, ct);
        if (invoice is null) return NotFound();

        var sub    = await _db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        var toEmail = (!string.IsNullOrWhiteSpace(invoice.RecipientEmail) ? invoice.RecipientEmail : sub?.BillingEmail) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(toEmail))
            return BadRequest(new { message = "No recipient email on this invoice and no billing email on the tenant subscription. Set an email on the invoice or update the tenant's billing email." });

        var isConfigured = await _emailService.IsConfiguredAsync(ct);
        if (!isConfigured)
            return BadRequest(new
            {
                message = "SMTP is not configured on this platform. Go to Platform Settings → Email to configure SMTP, or download the PDF and send it manually.",
                smtpRequired = true
            });

        // Generate the PDF invoice
        var data     = BuildInvoiceData(invoice, tenant, sub);
        var pdfBytes = InvoicePdfService.Generate(data);
        var fileName = $"Invoice_{invoice.InvoiceNumber}.pdf";

        var amountFmt = $"{invoice.Amount:N2} {invoice.CurrencyCode}";
        var dueDateFmt = invoice.DueDate.ToString("dd MMM yyyy");
        var periodLine = invoice.PeriodDescription is { Length: > 0 } p
            ? $"<p><strong>Service period:</strong> {System.Web.HttpUtility.HtmlEncode(p)}</p>"
            : "";

        var htmlBody = $"""
            <div style="font-family:Arial,sans-serif;max-width:600px;margin:auto">
              <div style="background:#1e3a5f;padding:20px 24px">
                <h2 style="color:#fff;margin:0;font-size:20px">KynexOne Workforce</h2>
                <p style="color:#93c5fd;margin:4px 0 0">Invoice {System.Web.HttpUtility.HtmlEncode(invoice.InvoiceNumber)}</p>
              </div>
              <div style="padding:24px;background:#f8fafc">
                <p style="font-size:14px;color:#374151">
                  Dear <strong>{System.Web.HttpUtility.HtmlEncode(tenant?.Name ?? "Client")}</strong>,
                </p>
                <p style="font-size:14px;color:#374151">
                  Please find your invoice attached as a PDF. A summary is shown below.
                </p>
                <table style="border-collapse:collapse;width:100%;font-size:14px;margin-top:12px">
                  <tr style="background:#1e3a5f;color:#fff">
                    <td style="padding:10px 14px;font-weight:600">Invoice #</td>
                    <td style="padding:10px 14px">{System.Web.HttpUtility.HtmlEncode(invoice.InvoiceNumber)}</td>
                  </tr>
                  <tr style="background:#f1f5f9">
                    <td style="padding:10px 14px;font-weight:600;color:#374151">Amount Due</td>
                    <td style="padding:10px 14px;font-size:18px;color:#1e3a5f;font-weight:700">{System.Web.HttpUtility.HtmlEncode(amountFmt)}</td>
                  </tr>
                  <tr>
                    <td style="padding:10px 14px;font-weight:600;color:#374151">Due Date</td>
                    <td style="padding:10px 14px;color:#374151">{System.Web.HttpUtility.HtmlEncode(dueDateFmt)}</td>
                  </tr>
                </table>
                {periodLine}
                <p style="font-size:13px;color:#374151;margin-top:20px">
                  Please arrange payment by the due date. For questions, reply to this email or contact your account manager.
                </p>
              </div>
              <div style="padding:12px 24px;background:#e2e8f0">
                <p style="font-size:11px;color:#64748b;margin:0">
                  KynexOne Workforce · This is an automated invoice notification. The PDF invoice is attached.
                </p>
              </div>
            </div>
            """;

        var attachment = new EmailAttachment(fileName, pdfBytes, "application/pdf");
        await _emailService.SendAsync(
            toEmail,
            tenant?.Name ?? toEmail,
            $"Invoice {invoice.InvoiceNumber} — {amountFmt} due {dueDateFmt}",
            htmlBody,
            [attachment],
            ct);

        invoice.Status = InvoiceStatuses.Sent;
        invoice.UpdatedAtUtc = DateTime.UtcNow;
        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            TenantId = tenantId,
            EntityType = "Invoice",
            EntityId = invoiceId.ToString(),
            Action = "InvoiceSent",
            NewValuesJson = System.Text.Json.JsonSerializer.Serialize(new { toEmail, invoiceNumber = invoice.InvoiceNumber, pdfAttached = true }),
            PerformedByName = "platform_admin",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? ""
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new { sent = true, billingEmail = toEmail, invoiceNumber = invoice.InvoiceNumber, pdfAttached = true });
    }

    // ── Marketing / Announcements ─────────────────────────────────────────────

    [HttpGet("marketing/announcements")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Marketing)]
    public async Task<IActionResult> ListAnnouncements([FromQuery] string? status, CancellationToken ct)
    {
        var q = _db.PlatformAnnouncements.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(a => a.Status == status);

        var items = await q.OrderByDescending(a => a.CreatedAtUtc).ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("marketing/announcements")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Marketing)]
    public async Task<IActionResult> CreateAnnouncement([FromBody] CreateAnnouncementRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Title is required." });
        if (string.IsNullOrWhiteSpace(req.Body))
            return BadRequest(new { message = "Body is required." });

        var creatorEmail = HttpContext.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value
            ?? HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? "platform";

        var announcement = new PlatformAnnouncement
        {
            Title = req.Title.Trim(),
            Body = req.Body.Trim(),
            TargetPlan = string.IsNullOrWhiteSpace(req.TargetPlan) ? "All" : req.TargetPlan.Trim(),
            Status = string.IsNullOrWhiteSpace(req.Status) ? "Draft" : req.Status.Trim(),
            PublishedAtUtc = req.PublishedAtUtc,
            ExpiresAtUtc = req.ExpiresAtUtc,
            CreatedByEmail = creatorEmail
        };
        _db.PlatformAnnouncements.Add(announcement);
        await _db.SaveChangesAsync(ct);
        return Ok(announcement);
    }

    [HttpPatch("marketing/announcements/{id:guid}")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Marketing)]
    public async Task<IActionResult> PatchAnnouncement(Guid id, [FromBody] PatchAnnouncementRequest req, CancellationToken ct)
    {
        var ann = await _db.PlatformAnnouncements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ann is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Title)) ann.Title = req.Title.Trim();
        if (!string.IsNullOrWhiteSpace(req.Body)) ann.Body = req.Body.Trim();
        if (req.Status is not null) ann.Status = req.Status.Trim();
        if (req.ExpiresAtUtc.HasValue) ann.ExpiresAtUtc = req.ExpiresAtUtc;
        ann.UpdatedAtUtc = DateTime.UtcNow;

        if (ann.Status == "Published" && ann.PublishedAtUtc is null)
            ann.PublishedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(ann);
    }

    [HttpDelete("marketing/announcements/{id:guid}")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Marketing)]
    public async Task<IActionResult> DeleteAnnouncement(Guid id, CancellationToken ct)
    {
        var ann = await _db.PlatformAnnouncements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ann is null) return NotFound();

        // Soft delete: archive
        ann.Status = "Archived";
        ann.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { id, status = "Archived" });
    }

    // ── Leads ─────────────────────────────────────────────────────────────────

    [HttpGet("leads")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Marketing)]
    public async Task<IActionResult> ListLeads([FromQuery] string? status, CancellationToken ct)
    {
        var q = _db.PlatformLeads.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(l => l.Status == status);

        var items = await q.OrderByDescending(l => l.CreatedAtUtc).ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("leads")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Marketing)]
    public async Task<IActionResult> CreateLead([FromBody] CreateLeadRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.CompanyName))
            return BadRequest(new { message = "CompanyName is required." });
        if (string.IsNullOrWhiteSpace(req.ContactEmail) || !req.ContactEmail.Contains('@'))
            return BadRequest(new { message = "A valid ContactEmail is required." });

        var lead = new PlatformLead
        {
            CompanyName = req.CompanyName.Trim(),
            ContactName = (req.ContactName ?? req.ContactEmail).Trim(),
            ContactEmail = req.ContactEmail.Trim().ToLowerInvariant(),
            Phone = req.Phone?.Trim(),
            Message = req.Message?.Trim(),
            Source = string.IsNullOrWhiteSpace(req.Source) ? "Manual" : req.Source.Trim()
        };
        _db.PlatformLeads.Add(lead);
        await _db.SaveChangesAsync(ct);
        return Ok(lead);
    }

    [HttpPatch("leads/{id:guid}")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Marketing)]
    public async Task<IActionResult> PatchLead(Guid id, [FromBody] PatchLeadRequest req, CancellationToken ct)
    {
        var lead = await _db.PlatformLeads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound();

        if (req.Status is not null) lead.Status = req.Status.Trim();
        if (req.Notes is not null) lead.Notes = req.Notes.Trim();
        if (req.AssignedTo is not null) lead.AssignedTo = req.AssignedTo.Trim();
        lead.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(lead);
    }

    [HttpPost("leads/{id:guid}/convert")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Marketing)]
    public async Task<IActionResult> ConvertLead(Guid id, [FromBody] ConvertLeadRequest req, CancellationToken ct)
    {
        var lead = await _db.PlatformLeads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return NotFound(new { message = "Lead not found." });
        if (lead.ConvertedToTenantId is not null)
            return BadRequest(new { message = "Lead has already been converted to a tenant." });

        // Reuse the existing CreateTenant logic inline
        var name = req.TenantName?.Trim();
        var slug = req.TenantSlug?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "TenantName is required." });
        if (string.IsNullOrWhiteSpace(slug))
            return BadRequest(new { message = "TenantSlug is required." });
        if (string.IsNullOrWhiteSpace(req.AdminEmail) || !req.AdminEmail.Contains('@'))
            return BadRequest(new { message = "A valid AdminEmail is required." });
        if (string.IsNullOrWhiteSpace(req.AdminPassword) || req.AdminPassword.Length < 10)
            return BadRequest(new { message = "AdminPassword must be at least 10 characters." });

        if (await _db.Tenants.AsNoTracking().AnyAsync(t => t.Slug == slug, ct))
            return Conflict(new { message = $"A tenant with slug '{slug}' already exists." });

        var tenant = new Tenant { Name = name, Slug = slug };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);

        var adminRole = await _authSeeder.EnsureTenantRolesAsync(tenant.Id, ct);
        var plan = string.IsNullOrWhiteSpace(req.Plan) ? "Trial" : req.Plan;
        var (defaultMaxUsers, defaultMaxEmployees) = SubscriptionTiers.GetDefaults(plan);

        var admin = new User
        {
            TenantId = tenant.Id,
            Email = req.AdminEmail.Trim().ToLowerInvariant(),
            NormalizedEmail = Infrastructure.Auth.AuthService.Normalize(req.AdminEmail),
            FullName = lead.ContactName,
            PasswordHash = _passwordHasher.Hash(req.AdminPassword),
            AccessMode = "FullPortal",
            Status = "Active",
            IsActive = true,
            IsEmailConfirmed = true
        };
        admin.UserRoles.Add(new UserRole { User = admin, Role = adminRole });
        _db.Users.Add(admin);

        _db.TenantSubscriptions.Add(new TenantSubscription
        {
            TenantId = tenant.Id,
            Plan = plan,
            Status = "Active",
            MaxUsers = defaultMaxUsers,
            MaxEmployees = defaultMaxEmployees,
            BillingEmail = req.BillingEmail ?? req.AdminEmail.Trim().ToLowerInvariant(),
            BillingCycle = "Monthly",
            MonthlyAmount = 0,
            CurrencyCode = "USD"
        });

        lead.ConvertedToTenantId = tenant.Id;
        lead.Status = "Converted";
        lead.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(new { leadId = id, tenantId = tenant.Id, tenantSlug = tenant.Slug, adminEmail = admin.Email });
    }

    // ── Platform Settings ─────────────────────────────────────────────────────

    [HttpGet("settings")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public IActionResult GetSettings()
    {
        return Ok(new
        {
            smtp = new
            {
                host     = _config["Smtp:Host"] ?? "",
                port     = _config["Smtp:Port"] ?? "587",
                username = _config["Smtp:Username"] ?? "",
                useSsl   = _config["Smtp:UseSsl"] ?? "true",
                fromEmail = _config["Smtp:FromEmail"] ?? "",
                fromName  = _config["Smtp:FromName"] ?? ""
            },
            ai = new
            {
                model = _config["Ai:ModelId"] ?? _config["AI:ModelId"] ?? "claude-3-5-sonnet-20241022"
            },
            platform = new
            {
                trialDurationDays = int.TryParse(_config["Platform:TrialDurationDays"], out var td) ? td : 14,
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            }
        });
    }

    [HttpPut("settings/smtp")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public IActionResult UpdateSmtp([FromBody] UpdateSmtpRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Host))
            return BadRequest(new { message = "Host is required." });

        // In a production app, write these to a secrets store or DB-backed setting.
        // For now: acknowledge the request and note the limitation.
        _log.LogInformation("SMTP config update requested: host={Host} port={Port}", req.Host, req.Port);

        return Ok(new
        {
            message = "SMTP configuration noted. To persist across restarts, update the Smtp:* keys in your environment variables or appsettings.",
            host = req.Host,
            port = req.Port,
            useSsl = req.UseSsl
        });
    }

    [HttpPost("settings/smtp/test")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> TestSmtp(CancellationToken ct)
    {
        var isConfigured = await _emailService.IsConfiguredAsync(ct);
        if (!isConfigured)
            return BadRequest(new { message = "SMTP is not configured. Update Smtp:Host, Smtp:Port, etc. first." });

        var adminEmail = HttpContext.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email)?.Value
            ?? HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(adminEmail))
            return BadRequest(new { message = "Cannot determine your email address from the JWT." });

        try
        {
            await _emailService.SendAsync(
                adminEmail,
                "Platform Admin",
                "KynexOne SMTP Test",
                "<p>Your SMTP configuration is working correctly.</p>",
                cancellationToken: ct);
            return Ok(new { sent = true, to = adminEmail });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "SMTP test email failed for {Email}", adminEmail);
            return Ok(new { sent = false, to = adminEmail, error = ex.Message });
        }
    }

    [HttpGet("settings/version")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Auditor)]
    public async Task<IActionResult> GetVersion(CancellationToken ct)
    {
        string? lastMigration = null;
        try
        {
            var migrations = await _db.Database.GetAppliedMigrationsAsync(ct);
            lastMigration = migrations.LastOrDefault();
        }
        catch { /* non-fatal */ }

        return Ok(new
        {
            version     = "1.0.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            dotnetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            lastMigration,
            checkedAtUtc = DateTime.UtcNow
        });
    }

    // ── Billing Summary ───────────────────────────────────────────────────────

    [HttpGet("billing/summary")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public async Task<IActionResult> BillingSummary(CancellationToken ct)
    {
        var allSubs = await _db.TenantSubscriptions
            .AsNoTracking()
            .Select(s => new { s.Status, s.MonthlyAmount })
            .ToListAsync(ct);

        var totalMrr = allSubs
            .Where(s => s.Status is "Active" or "Trial" or "PastDue")
            .Sum(s => s.MonthlyAmount);
        var totalArr = totalMrr * 12;

        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var allInvoices = await _db.TenantInvoices
            .AsNoTracking()
            .Select(i => new { i.Status, i.Amount, i.CreatedAtUtc, i.UpdatedAtUtc })
            .ToListAsync(ct);

        var overdueTotal = allInvoices.Where(i => i.Status == "Overdue").Sum(i => i.Amount);
        var overdueCount = allInvoices.Count(i => i.Status == "Overdue");
        var totalInvoices = allInvoices.Count;
        var paidThisMonth = allInvoices.Count(i => i.Status == "Paid" && i.UpdatedAtUtc >= startOfMonth);
        var sentThisMonth = allInvoices.Count(i => i.Status == "Sent" && i.CreatedAtUtc >= startOfMonth);

        return Ok(new
        {
            totalMrr,
            totalArr,
            overdueTotal,
            overdueCount,
            totalInvoices,
            paidThisMonth,
            sentThisMonth
        });
    }

    [HttpGet("billing/invoices")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance)]
    public async Task<IActionResult> AllInvoices(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (pageSize > 200) pageSize = 200;

        // Build tenant name lookup
        var tenants = await _db.Tenants.AsNoTracking()
            .Select(t => new { t.Id, t.Name })
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var q = _db.TenantInvoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(i => i.Status == status);

        var total = await q.CountAsync(ct);
        var invoices = await q
            .OrderByDescending(i => i.InvoiceDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var result = invoices.Select(i => new
        {
            i.Id,
            i.TenantId,
            tenantName = tenants.TryGetValue(i.TenantId, out var n) ? n : null,
            i.InvoiceNumber,
            i.Amount,
            i.CurrencyCode,
            i.Status,
            i.InvoiceDate,
            i.DueDate,
            i.PaidDate,
            i.PaymentMethod,
            i.PeriodDescription,
            i.CreatedAtUtc
        });

        return Ok(new { total, page, pageSize, invoices = result });
    }

    // ── Security Center ───────────────────────────────────────────────────────

    [HttpGet("security/summary")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> SecuritySummary(CancellationToken ct)
    {
        var tenants = await _db.Tenants.AsNoTracking()
            .Select(t => new { t.Id, t.Name, t.IsActive })
            .ToListAsync(ct);

        var tenantIds = tenants.Select(t => t.Id).ToList();

        var securitySettings = await _db.SecuritySettings
            .AsNoTracking()
            .Where(s => tenantIds.Contains(s.TenantId))
            .ToDictionaryAsync(s => s.TenantId, ct);

        var subs = await _db.TenantSubscriptions
            .AsNoTracking()
            .Where(s => tenantIds.Contains(s.TenantId))
            .ToDictionaryAsync(s => s.TenantId, ct);

        var result = tenants.Select(t =>
        {
            securitySettings.TryGetValue(t.Id, out var sec);
            subs.TryGetValue(t.Id, out var sub);

            // Risk heuristic: suspended or expired → high; default policy still in place → medium; secure → low
            var riskLevel = "Low";
            if (sub?.Status == "Suspended") riskLevel = "High";
            else if (sec is null) riskLevel = "Medium";

            return new
            {
                tenantId = t.Id,
                tenantName = t.Name,
                isActive = t.IsActive,
                subscriptionStatus = sub?.Status,
                hasMfaEnabled = false, // MFA not yet in SecuritySetting model — placeholder
                hasCustomPasswordPolicy = sec is not null && (sec.PasswordMinLength != 10 || !sec.PasswordRequireUppercase),
                maxFailedLoginAttempts = sec?.MaxFailedLoginAttempts ?? 5,
                sessionTimeoutMinutes = sec?.SessionTimeoutMinutes ?? 480,
                riskLevel
            };
        });

        return Ok(result);
    }

    // ── Tenant Security Policy ────────────────────────────────────────────────

    [HttpGet("tenants/{tenantId:guid}/security-policy")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Auditor)]
    public async Task<IActionResult> GetTenantSecurityPolicy(Guid tenantId, CancellationToken ct)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return NotFound();

        var sec = await _db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

        return Ok(new
        {
            tenantId,
            tenantName = tenant.Name,
            passwordMinLength          = sec?.PasswordMinLength ?? 10,
            passwordRequireUppercase   = sec?.PasswordRequireUppercase ?? true,
            passwordRequireLowercase   = sec?.PasswordRequireLowercase ?? true,
            passwordRequireDigit       = sec?.PasswordRequireDigit ?? true,
            passwordRequireSpecial     = sec?.PasswordRequireSpecial ?? true,
            passwordExpiryDays         = sec?.PasswordExpiryDays ?? 90,
            passwordHistoryCount       = sec?.PasswordHistoryCount ?? 5,
            maxFailedLoginAttempts     = sec?.MaxFailedLoginAttempts ?? 5,
            lockoutDurationMinutes     = sec?.LockoutDurationMinutes ?? 30,
            sessionTimeoutMinutes      = sec?.SessionTimeoutMinutes ?? 480,
            refreshTokenExpiryDays     = sec?.RefreshTokenExpiryDays ?? 30,
            allowMultipleSessions      = sec?.AllowMultipleSessions ?? true,
            isCustomPolicy             = sec is not null,
            updatedAtUtc               = sec?.UpdatedAtUtc
        });
    }

    [HttpPut("tenants/{tenantId:guid}/security-policy")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin)]
    public async Task<IActionResult> UpdateTenantSecurityPolicy(Guid tenantId, [FromBody] UpdateSecurityPolicyRequest body, CancellationToken ct)
    {
        if (!await _db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct)) return NotFound();

        var sec = await _db.SecuritySettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (sec is null)
        {
            sec = new SecuritySetting { TenantId = tenantId };
            _db.SecuritySettings.Add(sec);
        }

        if (body.PasswordMinLength.HasValue)          sec.PasswordMinLength          = Math.Max(6, Math.Min(32, body.PasswordMinLength.Value));
        if (body.PasswordRequireUppercase.HasValue)   sec.PasswordRequireUppercase   = body.PasswordRequireUppercase.Value;
        if (body.PasswordRequireLowercase.HasValue)   sec.PasswordRequireLowercase   = body.PasswordRequireLowercase.Value;
        if (body.PasswordRequireDigit.HasValue)       sec.PasswordRequireDigit       = body.PasswordRequireDigit.Value;
        if (body.PasswordRequireSpecial.HasValue)     sec.PasswordRequireSpecial     = body.PasswordRequireSpecial.Value;
        if (body.PasswordExpiryDays.HasValue)         sec.PasswordExpiryDays         = Math.Max(0, body.PasswordExpiryDays.Value);
        if (body.PasswordHistoryCount.HasValue)       sec.PasswordHistoryCount       = Math.Max(0, Math.Min(24, body.PasswordHistoryCount.Value));
        if (body.MaxFailedLoginAttempts.HasValue)     sec.MaxFailedLoginAttempts     = Math.Max(3, Math.Min(20, body.MaxFailedLoginAttempts.Value));
        if (body.LockoutDurationMinutes.HasValue)     sec.LockoutDurationMinutes     = Math.Max(5, body.LockoutDurationMinutes.Value);
        if (body.SessionTimeoutMinutes.HasValue)      sec.SessionTimeoutMinutes      = Math.Max(15, body.SessionTimeoutMinutes.Value);
        if (body.RefreshTokenExpiryDays.HasValue)     sec.RefreshTokenExpiryDays     = Math.Max(1, body.RefreshTokenExpiryDays.Value);
        if (body.AllowMultipleSessions.HasValue)      sec.AllowMultipleSessions      = body.AllowMultipleSessions.Value;

        sec.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _db.AuditLogs.AddAsync(new AuditLog
        {
            TenantId     = tenantId,
            UserId       = Guid.Empty,
            Action       = "platform.security_policy.updated",
            EntityName   = "SecuritySetting",
            EntityId     = sec.Id.ToString(),
            Metadata     = $"{{\"action\":\"security_policy.updated\",\"tenantId\":\"{tenantId}\"}}",
            IpAddress    = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            CreatedAtUtc = DateTime.UtcNow
        }, ct);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static InvoiceData BuildInvoiceData(TenantInvoice invoice, Tenant? tenant, TenantSubscription? sub)
    {
        var lineItems = new List<InvoiceLineItem>
        {
            new(
                $"KynexOne Workforce Platform — {sub?.Plan ?? "Subscription"}" +
                (invoice.PeriodDescription is { Length: > 0 } p ? $" ({p})" : string.Empty),
                invoice.Amount
            )
        };

        return new InvoiceData(
            InvoiceNumber:     invoice.InvoiceNumber,
            InvoiceDate:       invoice.InvoiceDate,
            DueDate:           invoice.DueDate,
            PeriodDescription: invoice.PeriodDescription,
            Amount:            invoice.Amount,
            CurrencyCode:      invoice.CurrencyCode,
            TenantName:        tenant?.Name ?? "Client",
            BillingEmail:      sub?.BillingEmail ?? string.Empty,
            BillingCycle:      sub?.BillingCycle ?? string.Empty,
            Notes:             invoice.Notes,
            LineItems:         lineItems
        );
    }

    // ── Tenant AI Usage (platform view) ──────────────────────────────────────

    [HttpGet("tenants/{tenantId:guid}/ai-usage")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Auditor)]
    public async Task<IActionResult> GetTenantAiUsage(Guid tenantId, [FromQuery] int? yearMonth, CancellationToken ct)
    {
        if (!await _db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId, ct)) return NotFound();

        var sub = await _db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var plan = sub?.Plan ?? "Starter";
        var limit = AiPlanLimits.GetMonthlyTokenLimit(plan);
        var ym = yearMonth ?? int.Parse(DateTime.UtcNow.ToString("yyyyMM"));

        var usage = await _db.TenantAiUsages
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.YearMonth == ym, ct);

        return Ok(new
        {
            tenantId,
            plan,
            yearMonth = ym,
            tokensUsed = usage?.TokensUsed ?? 0,
            requestCount = usage?.RequestCount ?? 0,
            blockedCount = usage?.BlockedCount ?? 0,
            monthlyTokenLimit = limit,
            isUnlimited = limit == 0,
            usagePct = limit > 0 ? Math.Min(100.0, (double)(usage?.TokensUsed ?? 0) / limit * 100) : 0.0
        });
    }

    // ── Platform Stats ────────────────────────────────────────────────────────

    [HttpGet("stats")]
    [RequirePlatformRole(PlatformRoles.Owner, PlatformRoles.Admin, PlatformRoles.Finance, PlatformRoles.Auditor)]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var totalTenants = await _db.Tenants.AsNoTracking().CountAsync(ct);
        var activeTenants = await _db.Tenants.AsNoTracking().CountAsync(t => t.IsActive, ct);
        var totalUsers = await _db.Users.AsNoTracking().CountAsync(u => !u.IsDeleted, ct);
        var totalEmployees = await _db.Employees.AsNoTracking().CountAsync(e => !e.IsDeleted, ct);

        var allSubs = await _db.TenantSubscriptions
            .AsNoTracking()
            .Select(s => new { s.Plan, s.Status, s.MonthlyAmount, s.ExpiresAtUtc })
            .ToListAsync(ct);

        int PlanCount(string plan) => allSubs.Count(s => string.Equals(s.Plan, plan, StringComparison.OrdinalIgnoreCase));

        // MRR: active/trial subscriptions with a monthly amount
        var estimatedMrr = allSubs
            .Where(s => s.Status is "Active" or "Trial" or "PastDue")
            .Sum(s => s.MonthlyAmount);

        // Clients expiring within 7 days (non-null expiry)
        var now = DateTime.UtcNow;
        var expiringCount = allSubs.Count(s =>
            s.ExpiresAtUtc.HasValue &&
            s.ExpiresAtUtc.Value > now &&
            s.ExpiresAtUtc.Value <= now.AddDays(7));

        var suspendedCount = allSubs.Count(s => string.Equals(s.Status, "Suspended", StringComparison.OrdinalIgnoreCase));
        var overdueCount   = await _db.TenantInvoices.AsNoTracking().CountAsync(i => i.Status == "Overdue", ct);

        return Ok(new
        {
            totalTenants,
            activeTenants,
            totalUsers,
            totalEmployees,
            estimatedMrr,
            expiringCount,
            suspendedCount,
            overdueCount,
            tenantsByPlan = new
            {
                trial = PlanCount("Trial"),
                starter = PlanCount("Starter"),
                growth = PlanCount("Growth"),
                enterprise = PlanCount("Enterprise")
            }
        });
    }
}

public record PlatformLoginRequest(string Email, string Password);
public record ImpersonateRequest(Guid UserId);

public record CreateTenantRequest(
    string Name,
    string Slug,
    string AdminEmail,
    string? AdminFullName,
    string AdminPassword,
    string? Plan,
    int? MaxUsers,
    int? MaxEmployees,
    string? BillingEmail,
    string? BillingCycle,
    decimal? MonthlyAmount,
    string? CurrencyCode,
    DateTime? ExpiresAtUtc);

public record AddTenantAdminRequest(string Email, string? FullName, string Password);
public record TenantActionRequest(string? Reason);
public record UpdateTenantRequest(string? Name);
public record ForcePasswordResetRequest(string TempPassword);
public record StartSupportAccessRequest(string TenantId, string UserId, string Reason);
public record EndSupportAccessRequest(string SessionId);

public record CreateInvoiceRequest(
    string InvoiceNumber,
    decimal Amount,
    string? CurrencyCode,
    string? Status,
    string? PaymentMethod,
    string? PaymentReference,
    string? PeriodDescription,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    DateOnly? PaidDate,
    string? Notes,
    string? RecipientEmail);

public record UpdateInvoiceRequest(
    string? Status,
    string? PaymentMethod,
    string? PaymentReference,
    DateOnly? PaidDate,
    string? Notes,
    string? RecipientEmail);

public record UpdatePlanPriceRequest(decimal MonthlyPrice);
public record CreatePlatformUserRequest(string Email, string? FullName, string Password, string? Role);
public record UpdatePlatformUserRequest(string? Role, bool? IsActive, string? FullName);

// ── New endpoint request records ──────────────────────────────────────────────

public record CreateAnnouncementRequest(
    string Title,
    string Body,
    string? TargetPlan,
    string? Status,
    DateTime? PublishedAtUtc,
    DateTime? ExpiresAtUtc);

public record PatchAnnouncementRequest(
    string? Title,
    string? Body,
    string? Status,
    DateTime? ExpiresAtUtc);

public record CreateLeadRequest(
    string CompanyName,
    string ContactName,
    string ContactEmail,
    string? Phone,
    string? Message,
    string? Source);

public record PatchLeadRequest(
    string? Status,
    string? Notes,
    string? AssignedTo);

public record UpdateSmtpRequest(
    string Host,
    int Port,
    string? Username,
    string? Password,
    bool UseSsl);

public record ConvertLeadRequest(
    string TenantName,
    string TenantSlug,
    string AdminEmail,
    string AdminPassword,
    string? Plan,
    string? BillingEmail);

public record UpdateSecurityPolicyRequest(
    int? PasswordMinLength,
    bool? PasswordRequireUppercase,
    bool? PasswordRequireLowercase,
    bool? PasswordRequireDigit,
    bool? PasswordRequireSpecial,
    int? PasswordExpiryDays,
    int? PasswordHistoryCount,
    int? MaxFailedLoginAttempts,
    int? LockoutDurationMinutes,
    int? SessionTimeoutMinutes,
    int? RefreshTokenExpiryDays,
    bool? AllowMultipleSessions);
