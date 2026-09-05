using System.Security.Cryptography;
using System.Text;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;

namespace Opstrax.Api.Controllers;

public static class MobileDeviceEndpoints
{
    public static void MapMobileDeviceEndpoints(WebApplication app)
    {
        app.MapPost("/api/mobile/devices/register", Register);
        app.MapPost("/api/mobile/devices/revoke", Revoke);
        app.MapGet("/api/mobile/devices", ListMine);
    }

    private static async Task<IResult> Register(
        HttpContext http,
        Dictionary<string, object?> body,
        Database db,
        CancellationToken ct)
    {
        if (!TryPrincipal(http, out var companyId, out var userId, out var denied)) return denied!;

        var token = Str(body, "token")?.Trim();
        var product = Str(body, "product")?.Trim().ToLowerInvariant();
        var platform = Str(body, "platform")?.Trim().ToLowerInvariant();
        var appVersion = CleanOptional(Str(body, "appVersion"), 40);
        var deviceOsVersion = CleanOptional(Str(body, "deviceOsVersion"), 80);

        if (!ValidExpoToken(token))
            return Results.BadRequest(ApiResponse<object>.Fail("Invalid push token"));
        if (product is not ("driver" or "fleet" or "customer"))
            return Results.BadRequest(ApiResponse<object>.Fail("product must be driver, fleet, or customer"));
        if (platform is not ("ios" or "android"))
            return Results.BadRequest(ApiResponse<object>.Fail("platform must be ios or android"));
        if (!ProductAllowed(http, product))
            return Results.Json(ApiResponse<object>.Fail("Forbidden", "This signed-in role cannot register the requested mobile product"), statusCode: StatusCodes.Status403Forbidden);

        var fingerprint = Fingerprint(token!);
        var row = await db.QuerySingleAsync(
            @"INSERT INTO mobile_device_tokens
                (company_id,user_id,product,platform,provider,push_token,token_fingerprint,app_version,device_os_version,status,last_registered_at,revoked_at,updated_at)
              VALUES (@company,@user,@product,@platform,'expo',@token,@fingerprint,@appVersion,@osVersion,'active',NOW(),NULL,NOW())
              ON CONFLICT (company_id,token_fingerprint)
              DO UPDATE SET user_id=EXCLUDED.user_id,
                            product=EXCLUDED.product,
                            platform=EXCLUDED.platform,
                            provider='expo',
                            push_token=EXCLUDED.push_token,
                            app_version=EXCLUDED.app_version,
                            device_os_version=EXCLUDED.device_os_version,
                            status='active',
                            revoked_at=NULL,
                            last_registered_at=NOW(),
                            updated_at=NOW()
              RETURNING id,product,platform,status,last_registered_at,created_at",
            c =>
            {
                c.Parameters.AddWithValue("@company", companyId);
                c.Parameters.AddWithValue("@user", userId);
                c.Parameters.AddWithValue("@product", product);
                c.Parameters.AddWithValue("@platform", platform);
                c.Parameters.AddWithValue("@token", token!);
                c.Parameters.AddWithValue("@fingerprint", fingerprint);
                c.Parameters.AddWithValue("@appVersion", (object?)appVersion ?? DBNull.Value);
                c.Parameters.AddWithValue("@osVersion", (object?)deviceOsVersion ?? DBNull.Value);
            }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            registered = true,
            id = row?["id"],
            product = row?["product"],
            platform = row?["platform"],
            status = row?["status"],
            lastRegisteredAt = row?["last_registered_at"]
        }, "Mobile device registered"));
    }

    private static async Task<IResult> Revoke(
        HttpContext http,
        Dictionary<string, object?> body,
        Database db,
        CancellationToken ct)
    {
        if (!TryPrincipal(http, out var companyId, out var userId, out var denied)) return denied!;
        var token = Str(body, "token")?.Trim();
        if (!ValidExpoToken(token))
            return Results.BadRequest(ApiResponse<object>.Fail("Invalid push token"));

        var fingerprint = Fingerprint(token!);
        var changed = await db.ExecuteAsync(
            @"UPDATE mobile_device_tokens
                 SET status='revoked',revoked_at=NOW(),updated_at=NOW()
               WHERE company_id=@company AND user_id=@user AND token_fingerprint=@fingerprint AND status='active'",
            c =>
            {
                c.Parameters.AddWithValue("@company", companyId);
                c.Parameters.AddWithValue("@user", userId);
                c.Parameters.AddWithValue("@fingerprint", fingerprint);
            }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new { revoked = changed > 0 }, changed > 0 ? "Mobile device revoked" : "Device was already revoked or not registered"));
    }

    private static async Task<IResult> ListMine(HttpContext http, Database db, CancellationToken ct)
    {
        if (!TryPrincipal(http, out var companyId, out var userId, out var denied)) return denied!;
        var rows = await db.QueryAsync(
            @"SELECT id,product,platform,provider,app_version,device_os_version,status,
                     LEFT(token_fingerprint,8) AS token_fingerprint_prefix,
                     last_registered_at,revoked_at,created_at,updated_at
                FROM mobile_device_tokens
               WHERE company_id=@company AND user_id=@user
               ORDER BY (status='active') DESC,last_registered_at DESC
               LIMIT 20",
            c =>
            {
                c.Parameters.AddWithValue("@company", companyId);
                c.Parameters.AddWithValue("@user", userId);
            }, ct);
        return Results.Ok(ApiResponse<object>.Ok(new { items = rows }));
    }

    private static bool TryPrincipal(HttpContext http, out long companyId, out long userId, out IResult? denied)
    {
        companyId = 0;
        userId = 0;
        denied = null;
        if (!http.Items.TryGetValue(EndpointMappings.AuthCompanyIdItemKey, out var company) || company is null ||
            !http.Items.TryGetValue(EndpointMappings.AuthUserIdItemKey, out var user) || user is null ||
            !long.TryParse(company.ToString(), out companyId) || companyId <= 0 ||
            !long.TryParse(user.ToString(), out userId) || userId <= 0)
        {
            denied = Results.Json(ApiResponse<object>.Fail("Unauthorized"), statusCode: StatusCodes.Status401Unauthorized);
            return false;
        }
        return true;
    }

    private static bool ProductAllowed(HttpContext http, string product)
    {
        var permissions = http.Items.TryGetValue(EndpointMappings.AuthPermissionsItemKey, out var raw) && raw is IEnumerable<string> values
            ? new HashSet<string>(values.Select(v => v.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var role = http.Items.TryGetValue(EndpointMappings.AuthRoleItemKey, out var roleValue)
            ? roleValue?.ToString()?.Trim().ToLowerInvariant() ?? string.Empty
            : string.Empty;

        if (product == "driver") return permissions.Contains("driver:self");
        if (product == "customer") return permissions.Contains("customer_portal:view") && !permissions.Contains("driver:self");
        if (product == "fleet")
        {
            var looksPlatformAdmin = role.Contains("platform", StringComparison.Ordinal) && role.Contains("admin", StringComparison.Ordinal);
            var looksCustomer = role.Contains("customer", StringComparison.Ordinal) && permissions.Contains("customer_portal:view");
            return !looksPlatformAdmin && !permissions.Contains("driver:self") && !looksCustomer;
        }
        return false;
    }

    private static bool ValidExpoToken(string? token)
        => token is not null
           && token.Length is >= 20 and <= 4096
           && !token.Any(char.IsWhiteSpace)
           && (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
           && token.EndsWith(']');

    private static string Fingerprint(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string? CleanOptional(string? value, int max)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean)) return null;
        return clean.Length <= max ? clean : clean[..max];
    }

    private static string? Str(Dictionary<string, object?> body, string key)
        => body.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;
}
