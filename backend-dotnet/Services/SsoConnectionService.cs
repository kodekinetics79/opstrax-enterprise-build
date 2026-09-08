using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// SsoConnectionService — Scoped
//
// Manages enterprise SSO connection configuration.
//
// SECURITY / PRODUCT-TRUTH DESIGN:
//   - client_secret is NEVER stored here. client_secret_ref holds a vault key name
//     or secret-manager reference (e.g. "vault://opstrax/tenant-42/oidc-secret").
//   - client_secret_ref is NEVER returned in API reads — only presence is indicated.
//   - Callers must configure the actual secret in their vault / secrets manager
//     and pass only the reference string here.
//   - OIDC has a real authorization-code + PKCE login implementation via
//     OidcLoginService and the /api/auth/sso/start + callback endpoints.
//   - SAML is representable as future configuration only. Until a real SAML
//     AuthnRequest/ACS/assertion-signature flow exists and is independently verified,
//     SAML connections MUST remain disabled and cannot be presented as usable SSO.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SsoConnectionService(Database db, AuditService audit, SecurityEventService secEvent)
{
    // Read — never expose client_secret_ref value, only whether it's configured
    public async Task<List<Dictionary<string, object?>>> GetForTenantAsync(
        long companyId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT id, company_id, provider_type, display_name,
                     issuer_or_entity_id, client_id,
                     (client_secret_ref IS NOT NULL AND client_secret_ref != '') AS has_secret_ref,
                     certificate_thumbprint, enabled, domain_hints, metadata_url,
                     created_at, updated_at, created_by
              FROM sso_connections
              WHERE company_id = @cid
              ORDER BY created_at DESC",
            c => c.Parameters.AddWithValue("@cid", companyId), ct);

        // Double-check: never return client_secret_ref — column excluded above
        foreach (var row in rows)
        {
            row.Remove("clientSecretRef");
        }

        return rows;
    }

    public async Task<long> CreateAsync(
        long companyId,
        SsoConnectionDto dto,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        ValidateDto(dto);

        var createdBy = http.Items.TryGetValue("opstrax.auth.user_id", out var uid)
            ? $"user:{uid}"
            : "system";

        var id = await db.InsertAsync(
            @"INSERT INTO sso_connections
                (company_id, provider_type, display_name, issuer_or_entity_id,
                 client_id, client_secret_ref, certificate_thumbprint,
                 enabled, domain_hints, metadata_url, created_by, created_at, updated_at)
              VALUES
                (@cid, @type, @name, @issuer,
                 @clientId, @secretRef, @certThumb,
                 @enabled, @hints::jsonb, @metaUrl, @createdBy, NOW(), NOW())",
            c =>
            {
                c.Parameters.AddWithValue("@cid",       companyId);
                c.Parameters.AddWithValue("@type",      dto.ProviderType);
                c.Parameters.AddWithValue("@name",      dto.DisplayName);
                c.Parameters.AddWithValue("@issuer",    dto.IssuerOrEntityId);
                c.Parameters.AddWithValue("@clientId",  dto.ClientId);
                c.Parameters.AddWithValue("@secretRef", (object?)dto.ClientSecretRef ?? DBNull.Value);
                c.Parameters.AddWithValue("@certThumb", (object?)dto.CertificateThumbprint ?? DBNull.Value);
                c.Parameters.AddWithValue("@enabled",   dto.Enabled);
                c.Parameters.AddWithValue("@hints",     (object?)dto.DomainHintsJson ?? DBNull.Value);
                c.Parameters.AddWithValue("@metaUrl",   (object?)dto.MetadataUrl ?? DBNull.Value);
                c.Parameters.AddWithValue("@createdBy", createdBy);
            }, ct);

        await audit.LogAsync(http, "sso.connection.created", "sso_connections", id, null, ct);
        await secEvent.LogAsync(companyId, null, "sso.config.changed", "medium",
            null, null, true,
            $"SSO connection created: {dto.DisplayName} ({dto.ProviderType})",
            new { connectionId = id, providerType = dto.ProviderType }, ct);

        return id;
    }

    public async Task UpdateAsync(
        long companyId, long id,
        SsoConnectionDto dto,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        ValidateDto(dto);

        // Ensure the connection belongs to this tenant
        var existing = await db.ScalarLongAsync(
            "SELECT id FROM sso_connections WHERE id = @id AND company_id = @cid LIMIT 1",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); }, ct);

        if (existing == 0) throw new InvalidOperationException("SSO connection not found or access denied");

        await db.ExecuteAsync(
            @"UPDATE sso_connections
              SET provider_type = @type, display_name = @name,
                  issuer_or_entity_id = @issuer, client_id = @clientId,
                  client_secret_ref = COALESCE(@secretRef, client_secret_ref),
                  certificate_thumbprint = COALESCE(@certThumb, certificate_thumbprint),
                  enabled = @enabled, domain_hints = @hints::jsonb,
                  metadata_url = @metaUrl, updated_at = NOW()
              WHERE id = @id AND company_id = @cid",
            c =>
            {
                c.Parameters.AddWithValue("@type",      dto.ProviderType);
                c.Parameters.AddWithValue("@name",      dto.DisplayName);
                c.Parameters.AddWithValue("@issuer",    dto.IssuerOrEntityId);
                c.Parameters.AddWithValue("@clientId",  dto.ClientId);
                c.Parameters.AddWithValue("@secretRef", (object?)dto.ClientSecretRef ?? DBNull.Value);
                c.Parameters.AddWithValue("@certThumb", (object?)dto.CertificateThumbprint ?? DBNull.Value);
                c.Parameters.AddWithValue("@enabled",   dto.Enabled);
                c.Parameters.AddWithValue("@hints",     (object?)dto.DomainHintsJson ?? DBNull.Value);
                c.Parameters.AddWithValue("@metaUrl",   (object?)dto.MetadataUrl ?? DBNull.Value);
                c.Parameters.AddWithValue("@id",        id);
                c.Parameters.AddWithValue("@cid",       companyId);
            }, ct);

        await audit.LogAsync(http, "sso.connection.updated", "sso_connections", id, null, ct);
        await secEvent.LogAsync(companyId, null, "sso.config.changed", "medium",
            null, null, true, $"SSO connection updated: {dto.DisplayName}", new { connectionId = id }, ct);
    }

    public async Task DisableAsync(
        long companyId, long id,
        Microsoft.AspNetCore.Http.HttpContext http,
        CancellationToken ct = default)
    {
        var rows = await db.ExecuteAsync(
            "UPDATE sso_connections SET enabled = false, updated_at = NOW() WHERE id = @id AND company_id = @cid",
            c => { c.Parameters.AddWithValue("@id", id); c.Parameters.AddWithValue("@cid", companyId); }, ct);

        if (rows == 0) throw new InvalidOperationException("SSO connection not found or access denied");

        await audit.LogAsync(http, "sso.connection.disabled", "sso_connections", id, null, ct);
        await secEvent.LogAsync(companyId, null, "sso.config.changed", "high",
            null, null, true, "SSO connection disabled", new { connectionId = id }, ct);
    }

    private static void ValidateDto(SsoConnectionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.DisplayName))
            throw new ArgumentException("display_name is required");
        if (dto.ProviderType is not ("oidc" or "saml"))
            throw new ArgumentException("provider_type must be 'oidc' or 'saml'");
        if (string.IsNullOrWhiteSpace(dto.IssuerOrEntityId))
            throw new ArgumentException("issuer_or_entity_id is required");
        if (string.IsNullOrWhiteSpace(dto.ClientId))
            throw new ArgumentException("client_id is required");

        // Product-truth boundary: the application has a real OIDC login flow but no
        // SAML AuthnRequest/ACS/assertion-validation implementation yet. Preserve SAML
        // configuration for future implementation, but never allow it to become an
        // enabled/advertised authentication path merely because a row exists.
        if (dto.ProviderType == "saml" && dto.Enabled)
            throw new ArgumentException(
                "SAML SSO is not yet an implemented authentication path; save the connection disabled until SAML login is implemented and verified.");
    }
}

public sealed class SsoConnectionDto
{
    public string ProviderType { get; init; } = "oidc";
    public string DisplayName { get; init; } = string.Empty;
    public string IssuerOrEntityId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    // Optional: vault reference key for the secret, not the secret value
    public string? ClientSecretRef { get; init; }
    public string? CertificateThumbprint { get; init; }
    public bool Enabled { get; init; } = true;
    public string? DomainHintsJson { get; init; }
    public string? MetadataUrl { get; init; }
}
