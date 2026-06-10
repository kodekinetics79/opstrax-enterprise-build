using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

    public PlatformController(ZayraDbContext db, IOptions<JwtOptions> jwt, IPasswordHasher passwordHasher, IAuthSeeder authSeeder)
    {
        _db = db;
        _jwt = jwt.Value;
        _passwordHasher = passwordHasher;
        _authSeeder = authSeeder;
    }

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

        var result = tenants.Select(t =>
        {
            subscriptions.TryGetValue(t.Id, out var sub);
            userCounts.TryGetValue(t.Id, out var users);
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
                    sub.ExpiresAtUtc
                },
                activeUserCount = users
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

        flag.IsEnabled = req.IsEnabled;
        flag.ConfigJson = req.ConfigJson;
        flag.UpdatedAtUtc = DateTime.UtcNow;

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
        await _db.SaveChangesAsync(ct);

        return Ok(new { user.Id, user.Email, user.FullName, tenantSlug = tenant.Slug });
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
