using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// OidcLoginService — OpenID Connect authorization-code login (PKCE).
//
// Turns a stored SSO connection (issuer + client_id + client_secret ref) into a
// real login: build the IdP authorize URL, then exchange the returned code and
// validate the id_token against the IdP's JWKS (fetched + rotated via discovery).
//
// This does NOT mint OpsTrax sessions — the endpoint layer does, so it can reuse
// the exact tenant-status / MFA / session-issue path that password login uses.
// This service only proves "the IdP authenticated this email".
// ─────────────────────────────────────────────────────────────────────────────
public sealed class OidcLoginService(IHttpClientFactory httpFactory)
{
    // One ConfigurationManager per metadata address: caches the discovery doc +
    // JWKS and refreshes on the library's schedule (handles Auth0 key rotation).
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> Managers = new();

    private static ConfigurationManager<OpenIdConnectConfiguration> Manager(string metadataAddress) =>
        Managers.GetOrAdd(metadataAddress, addr => new ConfigurationManager<OpenIdConnectConfiguration>(
            addr,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true }));

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(string metadataAddress, CancellationToken ct) =>
        Manager(metadataAddress).GetConfigurationAsync(ct);

    public string BuildAuthorizeUrl(
        OpenIdConnectConfiguration cfg, string clientId, string redirectUri,
        string state, string nonce, string codeChallenge)
    {
        var query = new Dictionary<string, string>
        {
            ["response_type"]         = "code",
            ["client_id"]             = clientId,
            ["redirect_uri"]          = redirectUri,
            ["scope"]                 = "openid email profile",
            ["state"]                 = state,
            ["nonce"]                 = nonce,
            ["code_challenge"]        = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        var qs = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"{cfg.AuthorizationEndpoint}?{qs}";
    }

    // Exchanges the auth code for tokens and fully validates the id_token
    // (signature via JWKS, issuer, audience, lifetime, and the flow nonce).
    // Returns the verified, lower-cased email. Throws on any validation failure.
    public async Task<string> ExchangeAndValidateEmailAsync(
        OpenIdConnectConfiguration cfg, string issuer, string clientId, string clientSecret,
        string redirectUri, string code, string codeVerifier, string expectedNonce, CancellationToken ct)
    {
        var client = httpFactory.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["redirect_uri"]  = redirectUri,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["code_verifier"] = codeVerifier,
        });

        using var resp = await client.PostAsync(cfg.TokenEndpoint, form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException("token_exchange_failed");

        string? idToken;
        using (var doc = JsonDocument.Parse(body))
            idToken = doc.RootElement.TryGetProperty("id_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(idToken))
            throw new InvalidOperationException("no_id_token");

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer               = issuer,
            ValidAudience             = clientId,
            IssuerSigningKeys         = cfg.SigningKeys,
            ValidateIssuer            = true,
            ValidateAudience          = true,
            ValidateLifetime          = true,
            ValidateIssuerSigningKey  = true,
            ClockSkew                 = TimeSpan.FromMinutes(2),
        });
        if (!result.IsValid)
            throw new InvalidOperationException("id_token_invalid");

        // Replay/binding guard: the id_token's nonce must match the flow nonce.
        var nonce = result.Claims.TryGetValue("nonce", out var n) ? n?.ToString() : null;
        if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal))
            throw new InvalidOperationException("nonce_mismatch");

        var email = result.Claims.TryGetValue("email", out var e) ? e?.ToString() : null;
        if (string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("no_email_claim");

        return email.Trim().ToLowerInvariant();
    }
}
