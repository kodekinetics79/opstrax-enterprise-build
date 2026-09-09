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

namespace Opstrax.Tests;

// Real Kestrel + bearer session + independently authenticated system lookup +
// signed tenant/user scope + restricted PostgreSQL RLS. Seed data is synthetic
// security-fixture data and makes no customer/provider/device certification claim.
[Collection("fleet-identity-schema")]
[Trait("Category", "Integration")]
public sealed class PrivateUserAuthorityHttpPostgresTests
{
    private const string Route = "/api/security/my-sessions";

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
            await using var host = await ParityHost.StartAsync(ProductionHandler());
            Assert.Equal(new[] { sessionA, sessionA2 }.Order(), (await Sessions(host.Client, tokenA)).Order());
            Assert.Equal([sessionB], await Sessions(host.Client, tokenB));
            Assert.Equal([sessionC], await Sessions(host.Client, tokenC));
            Assert.Equal(HttpStatusCode.Unauthorized, (await Get(host.Client, "unknown-" + suffix)).StatusCode);
        }
        finally
        {
            await ExecuteOwner("DELETE FROM user_sessions WHERE company_id=ANY(@companies)",
                ("companies", new[] { companyA, companyB }));
            await ExecuteOwner("DELETE FROM users WHERE company_id=ANY(@companies)",
                ("companies", new[] { companyA, companyB }));
            await ExecuteOwner("DELETE FROM companies WHERE id=ANY(@companies)",
                ("companies", new[] { companyA, companyB }));
        }
    }

    private static Delegate ProductionHandler()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Staging, Args = [] });
        builder.Configuration.Sources.Clear();
        using var app = builder.Build();
        app.MapOpsTraxEndpoints();
        var matches = new List<Delegate>();
        foreach (var source in ((IEndpointRouteBuilder)app).DataSources)
        {
            if (source.GetType().GetField("_routeEntries", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(source) is not IEnumerable entries) continue;
            foreach (var entry in entries)
            {
                if (entry is null || Member(entry, "RoutePattern") is not RoutePattern pattern || pattern.RawText != Route || Member(entry, "RouteHandler") is not Delegate handler) continue;
                if (Member(entry, "HttpMethods") is IEnumerable<string> methods && methods.Contains(HttpMethods.Get)) matches.Add(handler);
            }
        }
        return Assert.Single(matches);
    }

    private static object? Member(object value, string name) =>
        value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
        ?? value.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);

    private static async Task<long[]> Sessions(HttpClient client, string token)
    {
        using var response = await Get(client, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).Order().ToArray();
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Route);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        return await client.SendAsync(request);
    }

    private sealed class ParityHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<ParityHost> StartAsync(Delegate handler)
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
                    SELECT s.user_id,s.company_id,u.role_name
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
                http.Items[EndpointMappings.AuthPermissionsItemKey] = Array.Empty<string>();
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
            serving.MapGet(Route, handler);
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
        "INSERT INTO users(company_id,full_name,email,role_name,status,permissions_json) VALUES(@company,@name,@email,'Viewer','Active','[]'::jsonb) RETURNING id",
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

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
