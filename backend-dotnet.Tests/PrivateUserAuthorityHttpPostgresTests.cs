using System.Collections;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Real Kestrel + bearer session + independently authenticated system lookup +
// signed tenant/user scope + restricted PostgreSQL RLS. Seed data is synthetic
// security-fixture data and makes no customer/provider/device certification claim.
[Collection("fleet-identity-schema")]
[Trait("Category", "Integration")]
public sealed class PrivateUserAuthorityHttpPostgresTests
{
    private const string MySessionsRoute = "/api/security/my-sessions";
    private const string SessionRevokeRoute = "/api/security/my-sessions/{id:long}";
    private const string NotificationPrefsRoute = "/api/settings/notification-prefs";

    [Fact]
    public async Task MySessions_TwoUsersAndTwoTenants_ReturnOnlyAuthenticatedUsersRows()
    {
        await ReconcileTerminalPolicies();
        var suffix = Guid.NewGuid().ToString("N");
        var companyA = await InsertOwner(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'HTTP Principal A','Transportation') RETURNING id",
            ("code", "HPA-" + suffix));
        var companyB = await InsertOwner(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'HTTP Principal B','Transportation') RETURNING id",
            ("code", "HPB-" + suffix));
        var userA = await User(companyA, "A", suffix);
        var userB = await User(companyA, "B", suffix);
        var userC = await User(companyB, "C", suffix);
        var tokenA = "http-principal-a-" + suffix;
        var tokenA2 = "http-principal-a2-" + suffix;
        var tokenB = "http-principal-b-" + suffix;
        var tokenC = "http-principal-c-" + suffix;
        var sessionA = await Session(companyA, userA, tokenA);
        var sessionA2 = await Session(companyA, userA, tokenA2);
        var sessionB = await Session(companyA, userB, tokenB);
        var sessionC = await Session(companyB, userC, tokenC);

        try
        {
            await using var host = await ParityHost.StartAsync(ProductionHandlers());
            Assert.Equal(new[] { sessionA, sessionA2 }.Order(), (await Sessions(host.Client, tokenA)).Order());
            Assert.Equal([sessionB], await Sessions(host.Client, tokenB));
            Assert.Equal([sessionC], await Sessions(host.Client, tokenC));
            Assert.Equal(new[] { sessionA, sessionA2 }.Order(),
                (await Sessions(host.Client, tokenA,
                    $"{MySessionsRoute}?userId={userB}&companyId={companyB}")).Order());

            Assert.Equal(HttpStatusCode.NotFound,
                (await Delete(host.Client, tokenA, $"/api/security/my-sessions/{sessionB}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await Delete(host.Client, tokenA, $"/api/security/my-sessions/{sessionC}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await Delete(host.Client, tokenA, $"/api/security/my-sessions/{sessionA2}")).StatusCode);
            Assert.Equal([sessionA], await Sessions(host.Client, tokenA));
            Assert.Equal([sessionB], await Sessions(host.Client, tokenB));
            Assert.Equal([sessionC], await Sessions(host.Client, tokenC));

            using (var sameTenantPreferenceSpoof = await PutJson(host.Client, tokenA, NotificationPrefsRoute,
                JsonSerializer.Serialize(new
                {
                    userId = userB,
                    companyId = companyA,
                    email = new { enabled = false },
                    sms = new { enabled = true },
                })))
                Assert.Equal(HttpStatusCode.OK, sameTenantPreferenceSpoof.StatusCode);
            using (var crossTenantPreferenceSpoof = await PutJson(host.Client, tokenA, NotificationPrefsRoute,
                JsonSerializer.Serialize(new
                {
                    userId = userC,
                    companyId = companyB,
                    email = new { enabled = true },
                    sms = new { enabled = false },
                })))
                Assert.Equal(HttpStatusCode.OK, crossTenantPreferenceSpoof.StatusCode);
            var preferenceOwners = await QueryOwnerIds(
                "SELECT company_id,user_id FROM user_notification_prefs WHERE user_id=ANY(@users) ORDER BY user_id",
                ("users", new[] { userA, userB, userC }));
            Assert.Equal([(companyA, userA)], preferenceOwners);

            Assert.Equal(HttpStatusCode.Unauthorized, (await Get(host.Client, "unknown-" + suffix)).StatusCode);
        }
        finally
        {
            await ExecuteOwner("DELETE FROM audit_logs WHERE company_id=ANY(@companies)",
                ("companies", new[] { companyA, companyB }));
            await ExecuteOwner("DELETE FROM user_notification_prefs WHERE company_id=ANY(@companies)",
                ("companies", new[] { companyA, companyB }));
            await ExecuteOwner("DELETE FROM user_sessions WHERE company_id=ANY(@companies)",
                ("companies", new[] { companyA, companyB }));
            await ExecuteOwner("DELETE FROM users WHERE company_id=ANY(@companies)",
                ("companies", new[] { companyA, companyB }));
            await ExecuteOwner("DELETE FROM companies WHERE id=ANY(@companies)",
                ("companies", new[] { companyA, companyB }));
        }
    }

    private static IReadOnlyList<(string Method, string Pattern, Delegate Handler)> ProductionHandlers()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Staging, Args = [] });
        builder.Configuration.Sources.Clear();
        using var app = builder.Build();
        app.MapOpsTraxEndpoints();
        var required = new HashSet<(string Method, string Pattern)>
        {
            (HttpMethods.Get, MySessionsRoute),
            (HttpMethods.Delete, SessionRevokeRoute),
            (HttpMethods.Put, NotificationPrefsRoute),
        };
        var matches = new List<(string Method, string Pattern, Delegate Handler)>();
        foreach (var source in ((IEndpointRouteBuilder)app).DataSources)
        {
            if (source.GetType().GetField("_routeEntries", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(source) is not IEnumerable entries) continue;
            foreach (var entry in entries)
            {
                if (entry is null || Member(entry, "RoutePattern") is not RoutePattern routePattern || routePattern.RawText is not { } pattern ||
                    Member(entry, "RouteHandler") is not Delegate handler || Member(entry, "HttpMethods") is not IEnumerable<string> methods) continue;
                foreach (var method in methods)
                    if (required.Contains((method, pattern))) matches.Add((method, pattern, handler));
            }
        }
        Assert.Equal(required.OrderBy(item => item.Pattern).ThenBy(item => item.Method),
            matches.Select(item => (item.Method, item.Pattern)).OrderBy(item => item.Pattern).ThenBy(item => item.Method));
        return matches;
    }

    private static object? Member(object value, string name) =>
        value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
        ?? value.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);

    private static async Task<long[]> Sessions(HttpClient client, string token, string path = MySessionsRoute)
    {
        using var response = await Send(client, HttpMethod.Get, token, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).Order().ToArray();
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string token)
        => await Send(client, HttpMethod.Get, token, MySessionsRoute);

    private static async Task<HttpResponseMessage> Delete(HttpClient client, string token, string path)
        => await Send(client, HttpMethod.Delete, token, path);

    private static async Task<HttpResponseMessage> PutJson(HttpClient client, string token, string path, string json)
        => await Send(client, HttpMethod.Put, token, path, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    private static async Task<HttpResponseMessage> Send(
        HttpClient client, HttpMethod method, string token, string path, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        return await client.SendAsync(request);
    }

    private sealed class ParityHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<ParityHost> StartAsync(
            IReadOnlyList<(string Method, string Pattern, Delegate Handler)> handlers)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Staging,
                Args = []
            });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = Environments.Staging,
                ["Rls:EnforceTenantContext"] = "true",
                ["ConnectionStrings:DefaultConnection"] = TestDb.AppConnectionString,
                ["ConnectionStrings:SystemConnection"] = TestDb.SystemConnectionString,
                ["Rls:TenantTicketTtlSeconds"] = "120"
            });
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton<TenantScopeAccessor>();
            builder.Services.AddSingleton<Database>();
            builder.Services.AddSingleton<AuditService>();
            var serving = builder.Build();
            serving.Use(async (http, next) =>
            {
                var authorization = http.Request.Headers.Authorization.ToString();
                if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await http.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized"));
                    return;
                }
                var token = authorization["Bearer ".Length..].Trim();
                var db = http.RequestServices.GetRequiredService<Database>();
                var session = await db.QuerySingleInSystemScopeAsync(
                    """
                    SELECT s.user_id,s.company_id,u.role_name,u.permissions_json
                    FROM user_sessions s JOIN users u ON u.id=s.user_id AND u.company_id=s.company_id
                    WHERE s.session_token=@token AND s.expires_at>NOW() AND lower(u.status)='active'
                    LIMIT 1
                    """,
                    c => c.Parameters.AddWithValue("@token", token), http.RequestAborted);
                if (session is null)
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await http.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("Unauthorized"));
                    return;
                }

                var userId = Convert.ToInt64(session["userId"]);
                var companyId = Convert.ToInt64(session["companyId"]);
                http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
                http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
                http.Items[EndpointMappings.AuthRoleItemKey] = session["roleName"]?.ToString() ?? string.Empty;
                http.Items[EndpointMappings.AuthPermissionsItemKey] = Permissions(session["permissionsJson"]);
                var scopes = http.RequestServices.GetRequiredService<TenantScopeAccessor>();
                await using var principal = await db.BeginTenantScopeAsync(companyId, userId, http.RequestAborted);
                scopes.Current = principal;
                try
                {
                    var identity = await db.QuerySingleAsync(
                        "SELECT current_user role,opstrax_security.current_tenant_id() tenant,opstrax_security.current_user_id() principal",
                        ct: http.RequestAborted);
                    Assert.Equal("opstrax_app", identity!["role"]);
                    Assert.Equal(companyId, Convert.ToInt64(identity["tenant"]));
                    Assert.Equal(userId, Convert.ToInt64(identity["principal"]));
                    await next();
                    await principal.CompleteAsync(http.RequestAborted);
                }
                finally { scopes.Current = null; }
            });
            foreach (var (method, pattern, handler) in handlers)
                serving.MapMethods(pattern, [method], handler);
            await serving.StartAsync();
            var address = Assert.Single(serving.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses);
            var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(address),
                Timeout = TimeSpan.FromSeconds(15)
            };
            return new ParityHost(serving, client);
        }

        private static string[] Permissions(object? raw)
        {
            if (raw is null or DBNull) return [];
            using var json = JsonDocument.Parse(raw.ToString() ?? "[]");
            return json.RootElement.ValueKind == JsonValueKind.Array
                ? json.RootElement.EnumerateArray().Select(item => item.GetString()).Where(item => item is not null).Select(item => item!).ToArray()
                : [];
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static async Task ReconcileTerminalPolicies()
    {
        var root = FindRoot();
        foreach (var migration in new[]
        {
            "2026_09_07_stage112_camera_provider_ingest_spine.sql",
            "2026_09_07_stage115_device_compatibility_candidate_registry.sql",
            "2026_09_07_stage116_device_connectivity_profiles.sql",
            "2026_09_07_stage117_device_firmware_campaign_planning.sql",
            "2026_09_07_stage118_device_rma_replacement.sql",
            "2026_09_07_stage119_device_remote_command_governance.sql",
            "2026_09_07_stage120_device_connectivity_observations.sql",
            "2026_09_07_stage121_device_installation_work_packages.sql",
            "2026_09_07_stage122_installation_work_package_links.sql",
            "2026_09_07_stage123_device_retirement.sql",
            "2026_09_07_stage124_rma_support_ownership.sql",
            "2026_09_07_stage125_device_spare_pool.sql",
            "2026_09_07_stage126_device_support_tier_history.sql",
            "2026_09_08_stage128_device_compatibility_capability_catalog.sql",
            "2026_09_08_stage129_latest_device_signal_projection.sql",
            "2026_09_08_stage130_canonical_diagnostic_evidence_identity.sql",
            "2026_09_08_stage132_private_user_row_authority.sql"
        })
            await ExecuteOwner(File.ReadAllText(Path.Combine(root, "database", "migrations", migration)));
    }

    private static Task<long> User(long company, string label, string suffix) => InsertOwner(
        "INSERT INTO users(company_id,full_name,email,role_name,status,permissions_json) VALUES(@company,@name,@email,'Viewer','Active','[\"notifications:view\"]'::jsonb) RETURNING id",
        ("company", company), ("name", "HTTP Principal " + label),
        ("email", $"http-principal-{label}-{suffix}@example.invalid"));

    private static Task<long> Session(long company, long user, string token) => InsertOwner(
        "INSERT INTO user_sessions(company_id,user_id,session_token,expires_at) VALUES(@company,@user,@token,NOW()+INTERVAL '1 hour') RETURNING id",
        ("company", company), ("user", user), ("token", token));

    private static async Task<long> InsertOwner(string sql, params (string Key, object Value)[] values)
    {
        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (key, value) in values) command.Parameters.AddWithValue("@" + key, value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteOwner(string sql, params (string Key, object Value)[] values)
    {
        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        foreach (var (key, value) in values) command.Parameters.AddWithValue("@" + key, value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(long CompanyId, long UserId)[]> QueryOwnerIds(
        string sql, params (string Key, object Value)[] values)
    {
        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (key, value) in values) command.Parameters.AddWithValue("@" + key, value);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(long CompanyId, long UserId)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetInt64(0), reader.GetInt64(1)));
        return rows.ToArray();
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
