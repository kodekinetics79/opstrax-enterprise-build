using Microsoft.AspNetCore.Http;
using Opstrax.Api;
using Opstrax.Api.Controllers;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class EndpointMappingsSecurityHardeningTests
{
    [Fact]
    public void Login_UsesLockoutAndSecurityEventsBeforeSessionIssuance()
    {
        var login = MethodSource("Login(", "private static IResult InvalidCredentials");

        AssertOrdered(login, "CheckLockoutAsync", "VerifyPasswordHash");
        AssertOrdered(login, "userStatus", "VerifyPasswordHash");
        AssertOrdered(login, "RecordFailedLoginAsync", "RecordSuccessfulLoginAsync");
        AssertOrdered(login, "RecordSuccessfulLoginAsync", "var token =");
        Assert.Contains("return InvalidCredentials();", login, StringComparison.Ordinal);
        Assert.DoesNotContain("Account unavailable", login, StringComparison.Ordinal);
    }

    [Fact]
    public void Login_DoesNotIssueTokenWhenSessionPersistenceFails()
    {
        var login = MethodSource("Login(", "private static IResult InvalidCredentials");

        Assert.Contains("INSERT INTO user_sessions", login, StringComparison.Ordinal);
        Assert.DoesNotContain("SESSION-INSERT-FAIL", login, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (Exception _sessionEx)", login, StringComparison.Ordinal);
    }

    [Fact]
    public void BrowserAuthenticationFlows_OnlyConsumeMiddlewareCsrfToken()
    {
        var source = Source();
        var login = SourceBlock(source, "private static async Task<IResult> Login(", "private static IResult InvalidCredentials");
        var mfa = SourceBlock(source, "private static async Task<IResult> MfaLoginVerify(", "private static async Task<IResult> SsoDiscover(");
        var sso = SourceBlock(source, "private static async Task<IResult> SsoCallback(", "private static async Task<IResult> ForgotPassword(");
        var authMe = SourceBlock(source, "private static async Task<IResult> AuthMe(", "private static async Task<IResult> AuthRefresh(");
        var refresh = SourceBlock(source, "private static async Task<IResult> AuthRefresh(", "private static async Task<IResult> AuthLogout(");

        foreach (var flow in new[] { login, mfa, sso, authMe })
        {
            Assert.Contains("CurrentCsrfToken(http)", flow, StringComparison.Ordinal);
            Assert.DoesNotContain("RandomNumberGenerator.GetBytes(32)", flow, StringComparison.Ordinal);
            Assert.DoesNotContain("Request.Cookies[\"__CSRF_Token__\"]", flow, StringComparison.Ordinal);
        }

        Assert.Contains("csrf={Uri.EscapeDataString(csrfToken)}", sso, StringComparison.Ordinal);
        Assert.Contains("return await AuthMe(http, db, ct);", refresh, StringComparison.Ordinal);

        var helper = SourceBlock(source, "private static string CurrentCsrfToken(", "// GET /api/auth/me");
        Assert.Contains("CsrfMiddleware.TokenItemKey", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("Request.Cookies", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void TelemetryIngest_RequiresSignatureAndStoredSecret()
    {
        var ingest = MethodSource("private static async Task<IResult> TelemetryIngest", "// ── GET /api/telemetry/stream");

        AssertOrdered(ingest, "string.IsNullOrWhiteSpace(xSig)", "Look up device");
        Assert.Contains("hmac_secret_encrypted", ingest, StringComparison.Ordinal);
        Assert.Contains("DeviceHmacSecretProtection.ResolveForVerification", ingest, StringComparison.Ordinal);
        Assert.Contains("Device credentials are incomplete", ingest, StringComparison.Ordinal);
        Assert.Contains("allowLegacySecret", ingest, StringComparison.Ordinal);
        Assert.Contains("credential_slot", ingest, StringComparison.Ordinal);
        Assert.Contains("credential_match_count", ingest, StringComparison.Ordinal);
        Assert.Contains("credentialSlot == \"current\"", ingest, StringComparison.Ordinal);
        Assert.Contains("credentialSlot == \"previous\"", ingest, StringComparison.Ordinal);
        AssertOrdered(ingest, "TryParseObservedAt", "db.RunInSystemTransactionAsync");
        AssertOrdered(ingest, "db.RunInSystemTransactionAsync", "INSERT INTO telemetry_nonces");
        AssertOrdered(ingest, "INSERT INTO telemetry_nonces", "INSERT INTO location_events");
        Assert.Contains("ON CONFLICT DO NOTHING RETURNING id", ingest, StringComparison.Ordinal);
        Assert.DoesNotContain("skip HMAC", ingest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TelemetryHmac_WrongSignatureFailsConstantTimeComparison()
    {
        var signature = TelemetryHmacHelper.ComputeSignature(
            "test-secret"u8.ToArray(), "POST", "/api/telemetry/ingest",
            "1700000000", "nonce-1", TelemetryHmacHelper.Sha256Hex("{}"));

        Assert.True(TelemetryHmacHelper.ConstantTimeEquals(signature, signature));
        Assert.False(TelemetryHmacHelper.ConstantTimeEquals(signature, new string('0', signature.Length)));
        Assert.False(TelemetryHmacHelper.ConstantTimeEquals(signature, string.Empty));
    }

    [Theory]
    [InlineData("1700000000", 1700000000L)]
    [InlineData("1700000000000", 1700000000L)]
    [InlineData("2023-11-14T22:13:20Z", 1700000000L)]
    public void TrackerTimestamp_ParsesSecondsMillisecondsAndIso8601(string raw, long expectedSeconds)
    {
        Assert.True(EndpointMappings.TryParseTrackerTimestamp(raw, out var parsed));
        Assert.Equal(expectedSeconds, parsed.ToUnixTimeSeconds());
    }

    [Fact]
    public void GpsGatewayIngest_UsesGatewayAsCredentialAndDoesNotReturnDeviceIdentifiers()
    {
        var ingest = MethodSource("GpsTrackerIngest(", "// ── GET /api/telemetry/metrics");

        Assert.DoesNotContain("Telemetry:GatewaySecret", ingest, StringComparison.Ordinal);
        Assert.Contains("X-Gateway-Id", ingest, StringComparison.Ordinal);
        Assert.Contains("FROM telemetry_gateways", ingest, StringComparison.Ordinal);
        Assert.Contains("gatewayScopeCompanyId != companyId", ingest, StringComparison.Ordinal);
        Assert.Contains("FixedTimeEquals", ingest, StringComparison.Ordinal);
        Assert.Contains("last_heartbeat_at=NOW()", ingest, StringComparison.Ordinal);
        Assert.Contains("latest_vehicle_positions.event_time <= EXCLUDED.event_time", ingest, StringComparison.Ordinal);
        Assert.DoesNotContain("new { imei", ingest, StringComparison.Ordinal);
    }

    [Fact]
    public void AiFallback_DoesNotExposeExceptionMessages()
    {
        var aiAsk = MethodSource("AiAsk(", "private static Func<HttpContext");

        Assert.DoesNotContain("ex.Message", aiAsk, StringComparison.Ordinal);
        Assert.Contains("AI service temporarily unavailable.", aiAsk, StringComparison.Ordinal);
    }

    [Fact]
    public void SensitiveReadFamilies_RequirePermissionsAndTenantPredicates()
    {
        var source = Source();

        Assert.Contains("RequirePermission(http, \"dashcam:view\")", source, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, \"safety:evidence:view\")", source, StringComparison.Ordinal);
        Assert.Contains("RequirePermission(http, \"reports:view\")", source, StringComparison.Ordinal);
        Assert.Contains("WHERE de.id=@id AND de.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE se.id=@id AND se.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE ep.id=@id AND ep.company_id=@cid", source, StringComparison.Ordinal);
        Assert.Contains("WHERE tenant_id=@cid ORDER BY snapshot_date", source, StringComparison.Ordinal);
        Assert.Contains("WHERE company_id=@cid AND entity_name=@entity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PermissionGuard_DeniesMissingSensitivePermission()
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthRoleItemKey] = "Driver";
        http.Items[EndpointMappings.AuthPermissionsItemKey] = Array.Empty<string>();

        Assert.NotNull(EndpointMappings.RequirePermission(http, "safety:evidence:view"));
    }

    [Fact]
    public void TenantHelpersAndEvidenceReferences_RemainFailClosed()
    {
        var source = Source();

        Assert.DoesNotContain("await ModuleRecommendations(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("await AuditRows(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Task<List<Dictionary<string, object?>>> ModuleRecommendations(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private static Task<List<Dictionary<string, object?>>> AuditRows(", source, StringComparison.Ordinal);

        var createEvidence = SourceBlock(
            source,
            "private static async Task<IResult> CreateEvidencePackage(",
            "private static async Task<IResult> UpdateEvidencePackage(");
        Assert.Contains("incidents WHERE id=@incident AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("safety_events WHERE id=@safety AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("dashcam_events WHERE id=@dashcam AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("drivers WHERE id=@driver AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("vehicles WHERE id=@vehicle AND company_id=@cid", createEvidence, StringComparison.Ordinal);
        Assert.Contains("jobs WHERE id=@job AND company_id=@cid", createEvidence, StringComparison.Ordinal);
    }



    // ── NEW-R1-02: NULL role name must fail CLOSED (zero permissions), never admin ─
    [Fact]
    public async Task NullOrBlankRoleName_ResolvesZeroPermissions_NeverTheAdminWildcard()
    {
        // roleId 0 → the resolver never touches the DB, so a null Database is safe here.
        var user = new Dictionary<string, object?>
        {
            ["roleName"] = null,
            ["roleId"] = DBNull.Value,
            ["rolePermissionsJson"] = null,
            ["permissionsJson"] = null,
        };

        Assert.Empty(await EndpointMappings.ResolvePermissionsAsync(user, null!, CancellationToken.None));

        user["roleName"] = "   ";
        Assert.Empty(await EndpointMappings.ResolvePermissionsAsync(user, null!, CancellationToken.None));
    }

    [Fact]
    public void NoAuthPath_DefaultsAMissingRoleToCompanyAdmin()
    {
        // The fail-open was `user["roleName"] ?? "Company Admin"` in four auth paths.
        // None may return: a missing role is a data defect to repair, not a grant.
        Assert.DoesNotContain("?? \"Company Admin\"", Source(), StringComparison.Ordinal);
    }

    // ── DEF-006 gate-coverage source contract ─────────────────────────────────────
    // Every app.Map*("/api/...") registration in backend-dotnet/Controllers/*.cs must
    // resolve — directly, through the wrapper it is registered inside, or through the
    // named handler / factory it references (up to three hops) — to a recognized
    // AUTHORIZATION gate. Routes with no gate must appear in the explicit allowlist
    // below, each carrying a closed-enum reason and the concrete auth artifact that
    // protects it. The allowlist is exact and two-way: an entry that becomes gated (or
    // disappears) fails the test so the list can never accumulate stale exemptions.
    //
    // ── What this test was, and why it certified a falsehood (RETEST-20260821-1035-R1)
    // Five defects, each of which manufactured a false PASS:
    //
    //  1. SkipString() treated ' as a string delimiter. C# has no such thing: ' opens a
    //     CHAR literal. Every apostrophe in a comment ("the caller's tenant") swallowed
    //     source to the next apostrophe, so FindBalanced walked past the real closing
    //     brace: 31 indexed method bodies ran 30k–2,387,562 chars and inherited gate
    //     markers from wholly unrelated methods. Six routes were reported gated purely
    //     by that contamination (see the six PreSessionAuth/DeviceCredential/
    //     CapabilityToken entries marked "exposed at R1" below).
    //  2. Comments were matched as if they were code, so prose naming RequirePermission
    //     could gate a route on its own.
    //  3. The method-definition regex could not express a parenthesised return type, so
    //     24 definitions — every `Task<(long CompanyId, …, IResult? Denied)>` helper,
    //     which is exactly the shape a gate helper takes — were never indexed. The six
    //     /api/portal/* routes resolved through ResolvePrincipalAsync and were reported
    //     UNGATED although they call RequirePermission("customer_portal:view").
    //  4. Method bodies were indexed by bare NAME across the concatenated corpus and the
    //     longest body won. 53 names are defined in more than one controller file;
    //     GET /api/fleet-compliance/saudi/regions resolved into the same-named handler
    //     in a different file.
    //  5. Only the extracted CALL text was searched, so a route registered inside a
    //     wrapper — Guard(app.MapGet(...), "fleet:view") — lost its gate, and coverage
    //     stopped at EndpointMappings.cs: 683 of the 1,002 literal /api registrations.
    //
    // Recognized gates are AUTHORIZATION checks only. Tenant-entitlement wrappers
    // (RequireModule/RequirePack/RequireSaudiPack) answer "is this tenant sold the
    // feature", not "may this principal do this", and are deliberately NOT gates.
    private static readonly string[] GateMarkers =
    [
        "RequirePermission",            // canonical permission gate
        "RequireAnyDirectPermission",   // strict, alias-free permission gate
        "RequireInternalUser",          // internal-principal gate
        "RequireAdminPermission",       // audited admin permission gate
        "HasPermission(perms",          // ops:* handlers gate via the raw permission set
        "GetDriverIdFromAuthAsync",     // driver-portal: session identity IS the scope
        "ResolveCustomerIdForUserAsync",// customer-portal: bound customer IS the scope
        "IsCustomerPortalPrincipal",    // portal-boundary check
        "Guard(",                       // FleetTms/ColdChain endpoint filter → RequirePermission
        "GuardDriverTask(",             // FleetTms driver-or-staff-permission filter
        "RequireAsync(",                // PlatformEndpoints: authenticate + HasPlatformPermission
        "HasPlatformPermission(",       // platform permission set check
    ];

    private enum PublicReason
    {
        PreSessionAuth,             // no principal can exist yet; the flow IS the authentication
        SessionSelfService,         // identity comes from the caller's own session; acts only on it
        CapabilityToken,            // unauthenticated, but the URL token is the credential
        DeviceCredential,           // device/gateway HMAC over the request
        OpenToAnyAuthenticatedUser, // deliberately readable by every signed-in principal
        PlatformPrincipal,          // separate platform-admin principal space (PlatformEndpoints)
    }

    // Every entry names the CONCRETE artifact that protects the route. "Public by
    // design" with no named mechanism is not an acceptable justification and is
    // rejected by PublicAllowlist_DeclaresAConcreteAuthMechanism.
    private sealed record PublicRoute(string Route, PublicReason Reason, string Mechanism);

    private static readonly PublicRoute[] UngatedRouteAllowlist =
    [
        // ── Pre-session tenant authentication (no principal exists yet) ──────────
        new("POST /api/auth/login", PublicReason.PreSessionAuth,
            "password verification + lockout ledger; issues the session it is gating for"),
        new("POST /api/auth/forgot-password", PublicReason.PreSessionAuth,
            "email-addressed reset token; uniform response, no existence disclosure"),
        new("POST /api/auth/reset-password", PublicReason.PreSessionAuth,
            "single-use password_reset_tokens row is the credential"),
        new("GET /api/auth/sso/start/{id:long}", PublicReason.PreSessionAuth,
            "issues the data-protected opstrax_sso_flow state/nonce cookie; no data returned"),
        // exposed at R1 by fixing the ' char-literal bug:
        new("POST /api/auth/sso/discover", PublicReason.PreSessionAuth,
            "enumeration-safe domain lookup: identical payload for every input, fails OPEN "
            + "to the password field, returns no tenant data"),
        new("GET /api/auth/sso/callback", PublicReason.PreSessionAuth,
            "OIDC code exchange bound to the data-protected opstrax_sso_flow cookie: "
            + "FixedTimeEquals on state, nonce, 600s expiry, issuer validated against discovery"),
        new("GET /api/integrations/motive/oauth/callback", PublicReason.PreSessionAuth,
            "data-protected ten-minute Motive state is tenant/integration/generation-bound, "
            + "constant-time hash checked and atomically claimed once before provider I/O; "
            + "active actor permissions and module entitlement are revalidated in a short system transaction"),
        new("POST /api/auth/mfa/login-verify", PublicReason.PreSessionAuth,
            "HMAC-signed MfaChallengeService token (Jwt:Key) is the credential; "
            + "MfaChallengeConsumptionService makes it single-use"),

        // ── Session self-service (identity from the caller's own session) ────────
        new("GET /api/auth/me", PublicReason.SessionSelfService, "returns only the caller's own session claims"),
        new("POST /api/auth/refresh", PublicReason.SessionSelfService, "rotates only the caller's own session"),
        new("POST /api/auth/logout", PublicReason.SessionSelfService, "revokes only the caller's own session"),
        new("POST /api/auth/change-password", PublicReason.SessionSelfService,
            "requires the current password and acts on the caller's own user row"),
        // exposed at R1 by fixing the ' char-literal bug:
        new("POST /api/auth/mfa/enroll", PublicReason.SessionSelfService,
            "AuthUserIdItemKey or 401; refuses to rebind an already-active factor "
            + "(mfa_already_enabled) so a hijacked session cannot swap authenticators"),
        new("POST /api/auth/mfa/verify", PublicReason.SessionSelfService, "activates the caller's own enrolled factor"),
        new("GET /api/auth/mfa/status", PublicReason.SessionSelfService, "reports the caller's own factor"),
        new("GET /api/security/my-sessions", PublicReason.SessionSelfService, "lists only the caller's own sessions"),
        new("DELETE /api/security/my-sessions/{id:long}", PublicReason.SessionSelfService,
            "revokes a session scoped to the caller's own user id"),
        new("GET /api/localization/user-preferences", PublicReason.SessionSelfService, "the caller's own locale row"),

        // ── Capability tokens (unauthenticated; the URL token IS the credential) ─
        new("GET /api/public/detention/evidence/{token}", PublicReason.CapabilityToken,
            "DetentionReviewService.GetEvidenceByTokenAsync validates token, expiry and revocation"),
        new("GET /api/customer-eta/track/{trackingCode}", PublicReason.CapabilityToken,
            "customer_eta_links.secure_token AND public_status='Active' AND expires_at > NOW()"),
        // exposed at R1 by fixing the ' char-literal bug:
        new("GET /api/customer-visibility/tracking/{token}", PublicReason.CapabilityToken,
            "customer_visibility.public_tracking_token AND share_enabled AND expires_at > NOW() "
            + "AND visibility_status='active'"),
        new("GET /api/customer-visibility/tracking/{token}/events", PublicReason.CapabilityToken,
            "same public_tracking_token predicate as the parent route"),
        new("GET /api/customer-visibility/tracking/{token}/proofs", PublicReason.CapabilityToken,
            "same public_tracking_token predicate as the parent route"),
        // exposed at R1 by extending coverage past EndpointMappings.cs:
        new("GET /api/public/shipments/track/{token}", PublicReason.CapabilityToken,
            "fleet_tms_tracking_links.token_hash (hashed) AND is_revoked=false AND expires_at_utc > NOW()"),
        new("GET /api/public/shipments/track/{token}/events", PublicReason.CapabilityToken,
            "same hashed token_hash predicate; only visibility <> 'Private' events are returned"),
        new("GET /api/public/shipments/track/{token}/pod", PublicReason.CapabilityToken,
            "same hashed token_hash predicate; only status='Verified' PODs are returned"),
        new("GET /api/public/shipments/track/{token}/pod/{podId:long}/asset/{kind}", PublicReason.CapabilityToken,
            "podId is constrained by the company_id AND shipment_id the token resolves to, so it "
            + "cannot address another tenant's POD; asset host must be on the HTTPS allowlist"),

        // ── Device / gateway credentials ─────────────────────────────────────────
        new("POST /api/telemetry/ingest", PublicReason.DeviceCredential,
            "X-Device-Key + X-Timestamp + X-Nonce + X-Signature HMAC-SHA256 against "
            + "eld_devices.hmac_secret_encrypted, constant-time compared, nonce-replay ledgered"),
        // exposed at R1 by fixing the ' char-literal bug:
        new("POST /api/telemetry/gps-ingest", PublicReason.DeviceCredential,
            "X-Gateway-Id + per-gateway credential from telemetry_gateways, FixedTimeEquals; "
            + "the gateway's company scope must match the payload's"),
        new("POST /api/maintenance/fault-codes/ingest", PublicReason.DeviceCredential,
            "X-Device-Key + X-Signature HMAC-SHA256, constant-time compared"),

        // ── Deliberately open to any authenticated principal ─────────────────────
        new("GET /api/feature-flags/evaluate", PublicReason.OpenToAnyAuthenticatedUser,
            "flag evaluation scoped by GetCompanyId(http) + AuthUserIdItemKey; returns resolved flag "
            + "values only. The flag ADMIN surface GET /api/feature-flags is separately RequirePermission-gated"),
        new("GET /api/localization/countries", PublicReason.OpenToAnyAuthenticatedUser,
            "SELECT * FROM countries — global ISO reference data with no company_id column to scope by"),
        new("GET /api/localization/languages", PublicReason.OpenToAnyAuthenticatedUser,
            "SELECT * FROM languages — global ISO reference data with no company_id column to scope by"),
        new("GET /api/about/platform", PublicReason.OpenToAnyAuthenticatedUser,
            "build/version metadata assembled in process; reads no tenant table at all"),
        new("GET /api/about/health-summary", PublicReason.OpenToAnyAuthenticatedUser,
            "coarse Connected/Degraded from SELECT 1; the schema-scale fingerprinting this used "
            + "to leak (information_schema table counts, environment names) was removed"),
        new("GET /api/about/license", PublicReason.OpenToAnyAuthenticatedUser,
            "seat usage scoped by GetCompanyId(http) — the caller's own tenant only"),
        new("POST /api/security/export-requests", PublicReason.OpenToAnyAuthenticatedUser,
            "any user may REQUEST; the export itself is approval-governed and company-scoped"),

        // ── Platform-admin principal space (separate from tenant sessions) ───────
        // exposed at R1 by extending coverage past EndpointMappings.cs:
        new("POST /api/platform/auth/login", PublicReason.PlatformPrincipal,
            "pre-session platform authentication; failure counting via CountRecentAuthFailuresAsync"),
        new("POST /api/platform/auth/accept-invite", PublicReason.PlatformPrincipal,
            "single-use platform invite token; rate limited to MaxFailedLogins per email+IP"),
        new("POST /api/platform/auth/logout", PublicReason.PlatformPrincipal,
            "deletes only the platform_sessions row matching the caller's own bearer token"),
        new("GET /api/platform/auth/me", PublicReason.PlatformPrincipal,
            "AuthenticateAsync resolves the caller's own platform principal; returns nothing else"),
    ];

    [Fact]
    public void EveryApiRegistration_ResolvesToAnAuthorizationGate_OrIsExplicitlyPublic()
    {
        var index = ControllerRouteIndex();

        Assert.True(index.Registrations.Count > 950,
            $"Route extraction broke: only {index.Registrations.Count} registrations found across "
            + $"{index.FileCount} controller files (expected ~1,002)");
        Assert.True(index.Registrations.Count(r => r.File.EndsWith("EndpointMappings.cs", StringComparison.Ordinal)) > 650,
            "EndpointMappings.cs registration extraction collapsed");

        var allow = UngatedRouteAllowlist.Select(a => a.Route).ToHashSet(StringComparer.Ordinal);
        var ungated = index.Registrations
            .Where(r => !IsGated(r.Text, r.LocalBodies, index.GlobalBodies, 0, new HashSet<string>(StringComparer.Ordinal)))
            .Select(r => $"{r.Verb.ToUpperInvariant()} {r.Route}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        var unexpected = ungated.Where(r => !allow.Contains(r)).ToArray();
        Assert.True(unexpected.Length == 0,
            "Ungated /api registrations outside the public allowlist (add RequirePermission "
            + "or an equivalent gate — do NOT extend the allowlist for internal endpoints):\n  "
            + string.Join("\n  ", unexpected));

        // Two-way: every allowlist entry must still exist AND still be ungated.
        var ungatedSet = ungated.ToHashSet(StringComparer.Ordinal);
        var stale = allow.Where(a => !ungatedSet.Contains(a)).OrderBy(a => a, StringComparer.Ordinal).ToArray();
        Assert.True(stale.Length == 0,
            "Stale allowlist entries (route was gated, renamed, or removed — trim the list):\n  "
            + string.Join("\n  ", stale));
    }

    // ── Gate ORDERING (the presence-only hole) ────────────────────────────────────
    // IsGated only ever proved a gate EXISTS somewhere in the resolved body. A gate
    // placed after an early-return fast path passes that check while doing nothing.
    //
    // A source-level test cannot evaluate control flow, so this asserts the strongest
    // available lexical proxy: in the body where the gate marker actually appears, the
    // gate must precede BOTH (a) the first return of a success payload and (b) the
    // first database call. Establishing a tenant scope (BeginTenantScopeAsync) is
    // deliberately excluded from (b): TelemetryStream must open a scope so the gate's
    // own authorization_decision_logs insert can satisfy RLS, which is the gate working,
    // not a bypass. Known limits, stated so nobody over-reads a pass: a local function
    // declared above the gate is scored by its declaration position, and a gate reached
    // only on one branch is indistinguishable from one reached on all of them.
    [Fact]
    public void EveryGate_PrecedesTheFirstPayloadReturnAndDataAccess()
    {
        var index = ControllerRouteIndex();
        var payload = new System.Text.RegularExpressions.Regex(
            @"return\s+(?:await\s+)?(?:Typed)?Results?\.(?:Ok|Created|File|Bytes|Stream|Text|Content)\(|return\s+Ok\(|return\s+Created\(");
        var dataAccess = new System.Text.RegularExpressions.Regex(
            @"\bdb\.(?:QueryAsync|QuerySingleAsync|ExecuteAsync|InsertAsync|ScalarAsync|ScalarLongAsync|ScalarStringAsync|RunInSystemTransactionAsync)\s*\(");

        var lateGates = new List<string>();
        var gatedCount = 0;
        foreach (var reg in index.Registrations)
        {
            var body = ResolveGateBody(reg.Text, reg.LocalBodies, index.GlobalBodies, 0,
                new HashSet<string>(StringComparer.Ordinal));
            if (body is null) continue;
            gatedCount++;

            var gateAt = GateMarkers.Select(g => body.IndexOf(g, StringComparison.Ordinal))
                .Where(i => i >= 0).DefaultIfEmpty(-1).Min();
            if (gateAt < 0) continue;

            var name = $"{reg.Verb.ToUpperInvariant()} {reg.Route}";
            var firstPayload = payload.Match(body);
            if (firstPayload.Success && firstPayload.Index < gateAt)
                lateGates.Add($"{name} — returns a payload at offset {firstPayload.Index} before the gate at {gateAt}");
            var firstRead = dataAccess.Match(body);
            if (firstRead.Success && firstRead.Index < gateAt)
                lateGates.Add($"{name} — reads the database at offset {firstRead.Index} before the gate at {gateAt}");
        }

        Assert.True(gatedCount > 900, $"gate resolution collapsed: only {gatedCount} routes resolved to a gate");
        Assert.True(lateGates.Count == 0,
            "Authorization gates that do not run first — a fast path returns data or touches the "
            + "database before the gate, so the gate is decorative on that path:\n  "
            + string.Join("\n  ", lateGates.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)));
    }

    // ── Registrations the literal-route regex cannot see ─────────────────────────
    // MapGroup and MapMethods are used nowhere (asserted, so their arrival cannot be
    // silent). Five registrations do not present a literal "/api/…" first argument, so
    // they are enumerated here BY NAME and their gate is asserted individually. Growth
    // of this set breaks the count assertion rather than slipping past unmeasured.
    [Fact]
    public void NonLiteralRouteRegistrations_AreEnumeratedAndIndividuallyGated()
    {
        var index = ControllerRouteIndex();
        var all = new System.Text.RegularExpressions.Regex(@"app\.Map(?:Get|Post|Put|Delete|Patch)\s*\(");
        var literal = new System.Text.RegularExpressions.Regex(@"app\.Map(?:Get|Post|Put|Delete|Patch)\(\s*""/api/");

        var total = index.Sources.Sum(s => all.Matches(s.Value).Count);
        var literalCount = index.Sources.Sum(s => literal.Matches(s.Value).Count);
        Assert.Equal(5, total - literalCount);

        foreach (var source in index.Sources.Values)
        {
            Assert.DoesNotContain("MapGroup(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("MapMethods(", source, StringComparison.Ordinal);
        }

        // 1-4: MapDedicatedModule registers four interpolated $"/api/{moduleKey}" routes.
        //      GET/GET-detail gate inline; POST/PUT gate inside Create/UpdateModuleRecord.
        var mappings = Source();
        var dedicated = SourceBlock(mappings, "private static void MapDedicatedModule(", "private static async Task<IResult> GenericModule(");
        Assert.Contains("app.MapGet($\"/api/{moduleKey}\"", dedicated, StringComparison.Ordinal);
        Assert.Contains("app.MapGet($\"/api/{moduleKey}/{{id:long}}\"", dedicated, StringComparison.Ordinal);
        Assert.Contains("app.MapPost($\"/api/{moduleKey}\"", dedicated, StringComparison.Ordinal);
        Assert.Contains("app.MapPut($\"/api/{moduleKey}/{{id:long}}\"", dedicated, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(
            dedicated, @"RequirePermission\(http, ModuleReadPermissionByKey\[moduleKey\]\)").Count);
        foreach (var handler in new[] { "CreateModuleRecord", "UpdateModuleRecord" })
        {
            Assert.True(index.GlobalBodies.TryGetValue(handler, out var body), $"{handler} not indexed");
            Assert.Contains("RequirePermission(http, ModuleWritePermissionByKey[moduleKey])", body!, StringComparison.Ordinal);
        }

        // 5: CanonicalDemoResetEndpoints registers a const route. It is fail-closed by
        //    environment rather than by permission, and the handler repeats the check.
        var reset = ReadController("CanonicalDemoResetEndpoints.cs");
        Assert.Contains("internal const string Route = \"/api/platform/dev/reset-canonical-demo\";", reset, StringComparison.Ordinal);
        Assert.Contains("if (!app.Environment.IsDevelopment()) return;", reset, StringComparison.Ordinal);
        Assert.Contains("environment.IsDevelopment() && configuration.GetValue<bool>(\"DemoSeed:ResetEnabled\")", reset, StringComparison.Ordinal);
    }

    // ── The allowlist may not hide behind prose ──────────────────────────────────
    [Fact]
    public void PublicAllowlist_DeclaresAConcreteAuthMechanism()
    {
        var index = ControllerRouteIndex();

        foreach (var entry in UngatedRouteAllowlist)
        {
            Assert.True(Enum.IsDefined(entry.Reason), $"{entry.Route}: unknown PublicReason");
            Assert.True(entry.Mechanism.Length >= 25,
                $"{entry.Route}: the justification must name the concrete auth artifact, not restate 'public'.");
            Assert.DoesNotContain("public by design", entry.Mechanism, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(UngatedRouteAllowlist.Length, UngatedRouteAllowlist.Select(a => a.Route).Distinct(StringComparer.Ordinal).Count());

        // The unauthenticated-write category is verified mechanically, not by prose: an
        // ingest route that stopped comparing an HMAC in constant time would otherwise
        // keep its exemption.
        foreach (var entry in UngatedRouteAllowlist.Where(a => a.Reason == PublicReason.DeviceCredential))
        {
            var parts = entry.Route.Split(' ', 2);
            var reg = index.Registrations.FirstOrDefault(r =>
                string.Equals(r.Verb, parts[0], StringComparison.OrdinalIgnoreCase) && r.Route == parts[1]);
            Assert.False(reg is null, $"{entry.Route}: allowlisted device-credential route is not registered any more");
            var body = ResolveHandlerBody(reg!.Text, reg.LocalBodies, index.GlobalBodies)
                       ?? throw new Xunit.Sdk.XunitException($"{entry.Route}: handler body not resolvable");
            Assert.True(body.Contains("X-Signature", StringComparison.Ordinal) || body.Contains("X-Gateway-Id", StringComparison.Ordinal),
                $"{entry.Route}: no device/gateway credential header is read — it is not device-authenticated.");
            Assert.True(body.Contains("ConstantTimeEquals", StringComparison.Ordinal) || body.Contains("FixedTimeEquals", StringComparison.Ordinal),
                $"{entry.Route}: credential comparison is not constant-time.");
        }
    }

    // ── route index ──────────────────────────────────────────────────────────────

    private sealed record Registration(
        string Verb, string Route, string Text, string File, Dictionary<string, string> LocalBodies);

    private sealed record RouteIndex(
        List<Registration> Registrations,
        Dictionary<string, string> GlobalBodies,
        Dictionary<string, string> Sources,
        int FileCount);

    private static RouteIndex? _routeIndexCache;

    private static RouteIndex ControllerRouteIndex()
    {
        if (_routeIndexCache is not null) return _routeIndexCache;

        var controllers = Path.Combine(RepoRootDirectory(), "backend-dotnet", "Controllers");
        var files = Directory.GetFiles(controllers, "*.cs").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.True(files.Length >= 25, $"controller glob found only {files.Length} files");

        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var perFile = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var global = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            // Comments are stripped BEFORE anything is matched: prose that happens to
            // name RequirePermission must never gate a route.
            var source = StripComments(File.ReadAllText(file));
            sources[file] = source;
            var bodies = IndexMethodBodies(source);
            perFile[file] = bodies;
            foreach (var (name, body) in bodies)
                if (!global.TryGetValue(name, out var existing) || body.Length > existing.Length)
                    global[name] = body;
        }

        var registrations = new List<Registration>();
        var regex = new System.Text.RegularExpressions.Regex(@"app\.Map(Get|Post|Put|Delete|Patch)\(\s*""(/api/[^""]*)""");
        foreach (var file in files)
            foreach (System.Text.RegularExpressions.Match m in regex.Matches(sources[file]))
                registrations.Add(new Registration(
                    m.Groups[1].Value, m.Groups[2].Value,
                    // The whole STATEMENT, not just the Map call: a route registered as
                    // Guard(app.MapGet(...), "fleet:view") carries its gate outside the call.
                    StatementAround(sources[file], m.Index),
                    file, perFile[file]));

        return _routeIndexCache = new RouteIndex(registrations, global, sources, files.Length);
    }

    private static Dictionary<string, string> IndexMethodBodies(string source)
    {
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        // The return-type class MUST admit ( and ): every tuple-returning helper
        // (Task<(long CompanyId, …, IResult? Denied)>) is otherwise invisible, and that
        // is precisely the shape the gate helpers take.
        var defRegex = new System.Text.RegularExpressions.Regex(
            @"(?:private|internal|public)\s+static\s+(?:async\s+)?[\w<>,\?\[\]\.\(\) ]+?\b(\w+)\s*(?:<[^>\n]*>)?\(");
        foreach (System.Text.RegularExpressions.Match m in defRegex.Matches(source))
        {
            var name = m.Groups[1].Value;
            var paramsOpen = source.IndexOf('(', m.Index + m.Length - 1);
            if (paramsOpen < 0) continue;
            var paramsClose = FindBalanced(source, paramsOpen, '(', ')');
            var k = paramsClose + 1;
            while (k < source.Length && char.IsWhiteSpace(source[k])) k++;
            string body;
            if (k + 1 < source.Length && source[k] == '=' && source[k + 1] == '>')
            {
                var e = k;
                var depth = 0;
                while (e < source.Length)
                {
                    var c = source[e];
                    if (c == '"') { e = SkipStringLiteral(source, e) + 1; continue; }
                    if (c == '\'')
                    {
                        var close = SkipCharLiteral(source, e);
                        if (close != e) { e = close + 1; continue; }
                    }
                    if (c is '(' or '{' or '[') depth++;
                    else if (c is ')' or '}' or ']') depth--;
                    else if (c == ';' && depth <= 0) break;
                    e++;
                }
                body = source[k..Math.Min(e, source.Length)];
            }
            else
            {
                while (k < source.Length && source[k] != '{' && source[k] != ';') k++;
                if (k >= source.Length || source[k] == ';') continue;
                var e = FindBalanced(source, k, '{', '}');
                body = source[k..Math.Min(e + 1, source.Length)];
            }
            if (!bodies.TryGetValue(name, out var existing) || body.Length > existing.Length)
                bodies[name] = body;
        }
        return bodies;
    }

    // Own file first, global index second: 53 method names are defined in more than one
    // controller file, and the old name-only index let the longest same-named body win.
    private static string? LookupBody(string ident, Dictionary<string, string> local, Dictionary<string, string> global)
        => local.TryGetValue(ident, out var own) ? own : global.GetValueOrDefault(ident);

    private static bool IsGated(string text, Dictionary<string, string> local, Dictionary<string, string> global,
        int depth, HashSet<string> seen)
    {
        if (GateMarkers.Any(g => text.Contains(g, StringComparison.Ordinal))) return true;
        if (depth >= 3) return false;
        foreach (var ident in ReferencedIdentifiers(text))
        {
            if (!seen.Add(ident)) continue;
            var body = LookupBody(ident, local, global);
            if (body is not null && IsGated(body, local, global, depth + 1, seen)) return true;
        }
        return false;
    }

    // The body in which the gate marker literally appears (null when no gate resolves).
    private static string? ResolveGateBody(string text, Dictionary<string, string> local,
        Dictionary<string, string> global, int depth, HashSet<string> seen)
    {
        if (GateMarkers.Any(g => text.Contains(g, StringComparison.Ordinal))) return text;
        if (depth >= 3) return null;
        foreach (var ident in ReferencedIdentifiers(text))
        {
            if (!seen.Add(ident)) continue;
            var body = LookupBody(ident, local, global);
            if (body is null) continue;
            var found = ResolveGateBody(body, local, global, depth + 1, seen);
            if (found is not null) return found;
        }
        return null;
    }

    // The largest body reachable in one hop from a registration statement — the handler.
    private static string? ResolveHandlerBody(string text, Dictionary<string, string> local, Dictionary<string, string> global)
        => ReferencedIdentifiers(text)
            .Select(i => LookupBody(i, local, global))
            .Where(b => b is not null)
            .OrderByDescending(b => b!.Length)
            .FirstOrDefault();

    private static IEnumerable<string> ReferencedIdentifiers(string text)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(text, @"\b([A-Z]\w{3,})\s*\("))
            names.Add(m.Groups[1].Value);
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(text, @"[,(]\s*([A-Z]\w{3,})\s*[,)]"))
            names.Add(m.Groups[1].Value);
        return names;
    }

    // ── C# lexing ────────────────────────────────────────────────────────────────

    private static string StatementAround(string source, int index)
    {
        var start = index;
        while (start > 0 && source[start - 1] is not (';' or '{' or '}')) start--;
        var end = index;
        var depth = 0;
        while (end < source.Length)
        {
            var c = source[end];
            if (c == '"') { end = SkipStringLiteral(source, end) + 1; continue; }
            if (c == '\'')
            {
                var close = SkipCharLiteral(source, end);
                if (close != end) { end = close + 1; continue; }
            }
            if (c is '(' or '{' or '[') depth++;
            else if (c is ')' or '}' or ']') depth--;
            else if (c == ';' && depth <= 0) break;
            end++;
        }
        return source[start..Math.Min(end + 1, source.Length)];
    }

    private static int FindBalanced(string source, int openIndex, char open, char close)
    {
        var depth = 0;
        var j = openIndex;
        while (j < source.Length)
        {
            var c = source[j];
            if (c == '"') { j = SkipStringLiteral(source, j) + 1; continue; }
            if (c == '\'')
            {
                var end = SkipCharLiteral(source, j);
                if (end != j) { j = end + 1; continue; }
            }
            if (c == open) depth++;
            else if (c == close && --depth == 0) return j;
            j++;
        }
        return source.Length - 1;
    }

    private static int SkipStringLiteral(string source, int start)
    {
        // @"…" / $@"…" / @$"…" double a quote to escape it and ignore backslashes.
        var verbatim = start > 0 && (source[start - 1] == '@'
                                     || (start > 1 && source[start - 2] == '@' && source[start - 1] == '$')
                                     || (start > 1 && source[start - 2] == '$' && source[start - 1] == '@'));
        var j = start + 1;
        if (verbatim)
        {
            while (j < source.Length)
            {
                if (source[j] == '"')
                {
                    if (j + 1 < source.Length && source[j + 1] == '"') { j += 2; continue; }
                    return j;
                }
                j++;
            }
            return source.Length - 1;
        }
        while (j < source.Length && source[j] != '"')
        {
            if (source[j] == '\\') j++;
            j++;
        }
        return Math.Min(j, source.Length - 1);
    }

    // ' opens a CHAR literal in C#, never a string. Only 'x', '\n', '\'', '\xNN',
    // '\uXXXX' and '\UXXXXXXXX' count. Returning `start` means "this apostrophe is just
    // a character" — which is what an apostrophe in a comment is, and treating it as a
    // delimiter is the bug that produced 2.4 MB method bodies.
    private static int SkipCharLiteral(string source, int start)
    {
        var j = start + 1;
        if (j >= source.Length) return start;
        if (source[j] == '\\')
        {
            j++;
            if (j >= source.Length) return start;
            var esc = source[j];
            if (esc is 'u' or 'x' or 'U')
            {
                var max = esc == 'U' ? 8 : 4;
                j++;
                for (var k = 0; k < max && j < source.Length && Uri.IsHexDigit(source[j]); k++) j++;
            }
            else j++;
        }
        else j++;
        return j < source.Length && source[j] == '\'' ? j : start;
    }

    private static string StripComments(string source)
    {
        var sb = new System.Text.StringBuilder(source.Length);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '"')
            {
                var close = SkipStringLiteral(source, i);
                sb.Append(source[i..Math.Min(close + 1, source.Length)]);
                i = close + 1;
                continue;
            }
            if (c == '\'')
            {
                var close = SkipCharLiteral(source, i);
                if (close != i) { sb.Append(source[i..(close + 1)]); i = close + 1; continue; }
                sb.Append(' ');
                i++;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                var nl = source.IndexOf('\n', i);
                if (nl < 0) break;
                sb.Append('\n');
                i = nl + 1;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                sb.Append(' ');
                i = end < 0 ? source.Length : end + 2;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    private static string RepoRootDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadController(string fileName)
        => File.ReadAllText(Path.Combine(RepoRootDirectory(), "backend-dotnet", "Controllers", fileName));

    private static void AssertOrdered(string source, string first, string second)
    {
        var firstIndex = source.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = source.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Missing expected marker: {first}");
        Assert.True(secondIndex >= 0, $"Missing expected marker: {second}");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'");
    }

    private static string MethodSource(string startMarker, string endMarker)
    {
        var source = Source();
        return SourceBlock(source, startMarker, endMarker);
    }

    private static string SourceBlock(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Unable to locate source block {startMarker}");
        return source[start..end];
    }

    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "backend-dotnet", "Controllers", "EndpointMappings.cs"));
    }
}
