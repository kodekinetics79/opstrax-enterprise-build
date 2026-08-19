using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
// PLATFORM SETTINGS — /api/platform/settings/email
//
// Makes outbound mail configurable from the Platform Admin console. Before this,
// SMTP lived only in SMTP_* environment variables, so a platform admin could not
// turn email on without a redeploy — and every tenant-admin invite silently fell
// back to "no email sent", leaving the invited user Pending with nothing delivered.
//
// Security model:
//   • Behind the platform bearer guard, gated on platform:settings:manage. The
//     seeded super admin holds platform:* so it is covered; no other seeded role is.
//   • The SMTP password is write-only over the API: stored encrypted (AES-GCM via
//     PiiProtectionService) and NEVER echoed back. GET reports only whether one is
//     set. Submitting an empty password leaves the stored one untouched, so saving
//     an unrelated field cannot silently wipe the credential.
//   • Every save and every test send is audited. The password is never audited.
// ─────────────────────────────────────────────────────────────────────────────
public static class PlatformSettingsEndpoints
{
    private const string ManagePermission = "platform:settings:manage";

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/platform/settings/email", EmailSettingsGet);
        app.MapPut("/api/platform/settings/email", EmailSettingsSet);
        app.MapPost("/api/platform/settings/email/test", EmailSettingsTest);
        app.MapGet("/api/platform/settings/urls", AppUrlsGet);
        app.MapPut("/api/platform/settings/urls", AppUrlsSet);
    }

    private static async Task<IResult> EmailSettingsGet(
        HttpContext http, Database db, PlatformSettingsService settings, PlatformMailService mail, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, ManagePermission, ct);
        if (error is not null) return error;

        var config = await mail.ResolveAsync(ct);
        var (updatedBy, updatedAt) = await settings.LastUpdatedAsync(PlatformMailService.HostKey, ct);
        var storedInDb = await settings.HasStoredAsync(PlatformMailService.HostKey, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            host = config.Host,
            port = config.Port,
            username = config.Username,
            // Never returned. The console renders a "password is set" state instead.
            passwordSet = !string.IsNullOrWhiteSpace(config.Password),
            fromAddress = config.FromAddress,
            fromName = config.FromName,
            enableSsl = config.EnableSsl,
            configured = config.IsUsable,
            // False when no PII data key is configured, so the console can say WHY the password
            // field is unavailable instead of surfacing a save failure.
            canStorePassword = settings.EncryptionAvailable,
            // Tells the operator whether they are looking at console-managed values or the
            // deployment's environment variables — otherwise "why won't my edit stick?".
            source = storedInDb ? "database" : config.IsUsable ? "environment" : "unset",
            updatedBy,
            updatedAt,
        }, config.IsUsable ? "Email delivery is configured" : "Email delivery is not configured"));
    }

    private sealed record EmailSettingsRequest(
        string? Host, int? Port, string? Username, string? Password,
        string? FromAddress, string? FromName, bool? EnableSsl);

    private static async Task<IResult> EmailSettingsSet(
        HttpContext http, EmailSettingsRequest request, Database db,
        PlatformSettingsService settings, PlatformMailService mail, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, ManagePermission, ct);
        if (error is not null) return error;

        var host = request.Host?.Trim();
        var fromAddress = request.FromAddress?.Trim();

        // A host without a from-address (or vice versa) cannot send. Refuse the half-configured
        // state rather than storing something that silently fails at the next invite.
        if (!string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(fromAddress))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "A from address is required when a host is set"),
                statusCode: StatusCodes.Status400BadRequest);
        if (!string.IsNullOrWhiteSpace(fromAddress) && !fromAddress.Contains('@'))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "From address must be a valid email address"),
                statusCode: StatusCodes.Status400BadRequest);
        if (request.Port is { } port && (port <= 0 || port > 65535))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "Port must be between 1 and 65535"),
                statusCode: StatusCodes.Status400BadRequest);

        var actor = principal!.Email;
        await settings.SetAsync(PlatformMailService.HostKey, host, isSecret: false, actor, ct);
        await settings.SetAsync(PlatformMailService.PortKey, request.Port?.ToString(), isSecret: false, actor, ct);
        await settings.SetAsync(PlatformMailService.UserKey, request.Username?.Trim(), isSecret: false, actor, ct);
        await settings.SetAsync(PlatformMailService.FromKey, fromAddress, isSecret: false, actor, ct);
        await settings.SetAsync(PlatformMailService.FromNameKey, request.FromName?.Trim(), isSecret: false, actor, ct);
        await settings.SetAsync(PlatformMailService.SslKey, request.EnableSsl?.ToString().ToLowerInvariant(), isSecret: false, actor, ct);

        // Empty password = "leave what is stored alone", so editing the host does not require
        // re-typing the credential. Clearing it is an explicit action on the client.
        if (!string.IsNullOrEmpty(request.Password))
        {
            // Fail closed: an SMTP credential is never worth writing to the database in the clear.
            if (!settings.EncryptionAvailable)
                return Results.Json(ApiResponse<object>.Fail("Cannot store the SMTP password",
                        "Secret storage needs the PII data key (DATA_ENCRYPTION_KEY, or Pii__DataKey). Configure it, or supply the password " +
                        "through the SMTP_PASS environment variable instead — the other settings above were saved."),
                    statusCode: StatusCodes.Status400BadRequest);
            await settings.SetAsync(PlatformMailService.PasswordKey, request.Password, isSecret: true, actor, ct);
        }

        var config = await mail.ResolveAsync(ct);
        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.settings.email.updated", "PlatformSettings", null, null,
            new { host, port = config.Port, username = request.Username, fromAddress, enableSsl = config.EnableSsl, passwordChanged = !string.IsNullOrEmpty(request.Password) }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new { configured = config.IsUsable }, "Email settings saved"));
    }

    private sealed record EmailTestRequest(string? To);

    private static async Task<IResult> EmailSettingsTest(
        HttpContext http, EmailTestRequest request, Database db, PlatformMailService mail, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, ManagePermission, ct);
        if (error is not null) return error;

        var to = string.IsNullOrWhiteSpace(request.To) ? principal!.Email : request.To!.Trim();
        if (!to.Contains('@'))
            return Results.Json(ApiResponse<object>.Fail("Validation failed", "A valid recipient email is required"),
                statusCode: StatusCodes.Status400BadRequest);

        var (sent, failure) = await mail.SendAsync(
            to,
            "OpsTrax — SMTP test message",
            $"""
            This is a test message from the OpsTrax Platform Admin console.

            If you received it, outbound email is working and tenant administrators
            will receive their activation invites.

            Sent by: {principal!.Email}
            """,
            ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.settings.email.test", "PlatformSettings", null, null,
            new { to, sent, failure }, ct);

        // The SMTP failure reason is returned deliberately: without it the operator cannot tell
        // a wrong password from a blocked port from an unverified sender address.
        return sent
            ? Results.Ok(ApiResponse<object>.Ok(new { sent = true, to }, $"Test email sent to {to}"))
            : Results.Json(ApiResponse<object>.Fail("Test email failed", failure ?? "Unknown SMTP error"),
                statusCode: StatusCodes.Status400BadRequest);
    }

    // ── Public application URLs ─────────────────────────────────────────────────
    // The tenant app URL is what an activation link points at. With it unset, an invite
    // can neither be emailed nor handed over as a link, so it belongs in the console
    // next to SMTP rather than only in deployment environment variables.

    private static async Task<IResult> AppUrlsGet(
        HttpContext http, Database db, PlatformSettingsService settings, CancellationToken ct)
    {
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, ManagePermission, ct);
        if (error is not null) return error;

        var tenantUrl = await settings.GetTenantAppUrlAsync(ct);
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            tenantAppUrl = tenantUrl,
            platformAppUrl = await settings.GetPlatformAppUrlAsync(ct),
            tenantUrlSource = await settings.HasStoredAsync(PlatformSettingsService.TenantAppUrlKey, ct)
                ? "database"
                : tenantUrl is null ? "unset" : "environment",
        }));
    }

    private sealed record AppUrlsRequest(string? TenantAppUrl, string? PlatformAppUrl);

    private static async Task<IResult> AppUrlsSet(
        HttpContext http, AppUrlsRequest request, Database db, PlatformSettingsService settings, CancellationToken ct)
    {
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, ManagePermission, ct);
        if (error is not null) return error;

        foreach (var url in new[] { request.TenantAppUrl, request.PlatformAppUrl })
        {
            if (!string.IsNullOrWhiteSpace(url) && !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
                return Results.Json(ApiResponse<object>.Fail("Validation failed", $"'{url}' is not a valid absolute URL"),
                    statusCode: StatusCodes.Status400BadRequest);
            else if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var ok)
                     && ok.Scheme != Uri.UriSchemeHttp && ok.Scheme != Uri.UriSchemeHttps)
                return Results.Json(ApiResponse<object>.Fail("Validation failed", "Application URLs must be http or https"),
                    statusCode: StatusCodes.Status400BadRequest);
        }

        var actor = principal!.Email;
        await settings.SetAsync(PlatformSettingsService.TenantAppUrlKey, request.TenantAppUrl?.Trim().TrimEnd('/'), isSecret: false, actor, ct);
        await settings.SetAsync(PlatformSettingsService.PlatformAppUrlKey, request.PlatformAppUrl?.Trim().TrimEnd('/'), isSecret: false, actor, ct);

        await PlatformEndpoints.AuditAsync(db, principal!, http, "platform.settings.urls.updated", "PlatformSettings", null, null,
            new { request.TenantAppUrl, request.PlatformAppUrl }, ct);

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            tenantAppUrl = await settings.GetTenantAppUrlAsync(ct),
            platformAppUrl = await settings.GetPlatformAppUrlAsync(ct),
        }, "Application URLs saved"));
    }
}
