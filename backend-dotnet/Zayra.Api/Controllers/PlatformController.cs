using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Zayra.Api.Application.Auth;
using Zayra.Api.Application.Common;
using Zayra.Api.Data;
using Zayra.Api.Domain.Entities;
using Zayra.Api.Infrastructure.Auth;
using Zayra.Api.Infrastructure.Email;
using Zayra.Api.Infrastructure.Subscriptions;
using Zayra.Api.Models;

namespace Zayra.Api.Controllers;

[ApiController]
[Route("api/platform")]
public class PlatformController : ControllerBase
{
    private readonly ZayraDbContext _db;
    private readonly JwtOptions _jwt;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuthSeeder _authSeeder;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly ILogger<PlatformController> _log;

    public PlatformController(
        ZayraDbContext db,
        IOptions<JwtOptions> jwt,
        IPasswordHasher passwordHasher,
        IAuthSeeder authSeeder,
        ITokenService tokenService,
        IEmailService emailService,
        ILogger<PlatformController> log)
    {
        _db = db;
        _jwt = jwt.Value;
        _passwordHasher = passwordHasher;
        _authSeeder = authSeeder;
        _tokenService = tokenService;
        _emailService = emailService;
        _log = log;
    }

    private string PlatformAdminEmail =>
        Environment.GetEnvironmentVariable("PLATFORM_ADMIN_EMAIL") ?? string.Empty;

    // ── Auth ─────────────────────────────────────────────────────────────────

    [HttpPost("auth/login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] PlatformLoginRequest req)
    {
        var expectedEmail = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_EMAIL");
        var expectedPassword = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_PASSWORD");

        if (string.IsNullOrWhiteSpace(expectedEmail) || string.IsNullOrWhiteSpace(expectedPassword))
            return StatusCode(503, new { message = "Platform admin credentials are not configured." });

        if (!string.Equals(req.Email, expectedEmail, StringComparison.OrdinalIgnoreCase) ||
            req.Password != expectedPassword)
            return Unauthorized(new { message = "Invalid platform admin credentials." });

        var expiresAt = DateTime.UtcNow.AddHours(8);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "platform-admin"),
            new(JwtRegisteredClaimNames.Email, expectedEmail),
            new(ClaimTypes.Role, "PlatformAdmin"),
            new("is_platform_admin", "true"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, expires: expiresAt, signingCredentials: credentials);
        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new { token = tokenString, expiresAt });
    }

    // ── Tenants List ─────────────────────────────────────────────────────────

    [HttpGet("tenants")]
    [Authorize(Policy = "PlatformAdmin")]
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
                    sub.ExpiresAtUtc
                },
                activeUserCount = users,
                activeEmployeeCount = emps
            };
        });

        return Ok(result);
    }

    // ── Tenant Detail ─────────────────────────────────────────────────────────

    [HttpGet("tenants/{tenantId:guid}")]
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
        var logs = await q
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new { total, page, pageSize, logs });
    }

    // ── Plans Catalog ─────────────────────────────────────────────────────────

    [HttpGet("plans")]
    [Authorize(Policy = "PlatformAdmin")]
    public IActionResult GetPlans()
    {
        // Static plan definitions — these are the source of truth for the frontend plan picker.
        // To customise limits for a specific tenant, use PATCH /tenants/{id}/subscription.
        var plans = new[]
        {
            new { Name = "Trial",      MaxUsers = 3,   MaxEmployees = 10,  MonthlyPrice = 0m,   Description = "Free evaluation. No payment required. 10 employees, 3 users, core modules only." },
            new { Name = "Starter",    MaxUsers = 10,  MaxEmployees = 50,  MonthlyPrice = 149m, Description = "Up to 50 employees. Core HR, attendance, leave, payroll." },
            new { Name = "Growth",     MaxUsers = 50,  MaxEmployees = 250, MonthlyPrice = 499m, Description = "Up to 250 employees. All Starter modules plus recruitment, performance, loans, analytics." },
            new { Name = "Enterprise", MaxUsers = 0,   MaxEmployees = 0,   MonthlyPrice = 0m,   Description = "Unlimited employees and users. All modules, custom branding, dedicated support. Custom pricing." },
        };
        return Ok(plans);
    }

    // ── Support Access (structured break-glass) ───────────────────────────────

    [HttpPost("support-access/start")]
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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
            Notes = req.Notes?.Trim()
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
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
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

    // ── Tenant AI Usage (platform view) ──────────────────────────────────────

    [HttpGet("tenants/{tenantId:guid}/ai-usage")]
    [Authorize(Policy = "PlatformAdmin")]
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
    [Authorize(Policy = "PlatformAdmin")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var totalTenants = await _db.Tenants.AsNoTracking().CountAsync(ct);
        var activeTenants = await _db.Tenants.AsNoTracking().CountAsync(t => t.IsActive, ct);
        var totalUsers = await _db.Users.AsNoTracking().CountAsync(u => !u.IsDeleted, ct);
        var totalEmployees = await _db.Employees.AsNoTracking().CountAsync(e => !e.IsDeleted, ct);

        var planCounts = await _db.TenantSubscriptions
            .AsNoTracking()
            .GroupBy(s => s.Plan)
            .Select(g => new { Plan = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int PlanCount(string plan) => planCounts.FirstOrDefault(p => string.Equals(p.Plan, plan, StringComparison.OrdinalIgnoreCase))?.Count ?? 0;

        return Ok(new
        {
            totalTenants,
            activeTenants,
            totalUsers,
            totalEmployees,
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
    string? Notes);

public record UpdateInvoiceRequest(
    string? Status,
    string? PaymentMethod,
    string? PaymentReference,
    DateOnly? PaidDate,
    string? Notes);
