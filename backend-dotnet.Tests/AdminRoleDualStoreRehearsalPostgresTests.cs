using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Security;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

/// <summary>
/// Disposable rehearsal for CR-W1-A02-002. This exercises the registered tenant role-update
/// handler with the restricted application identity and signed tenant transaction. It never
/// targets the staging tenant or the live role 17.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AdminRoleDualStoreRehearsalPostgresTests
{
    private static readonly string[] Candidate =
    [
        "alerts:acknowledge", "alerts:close", "alerts:view", "compliance:view", "dashboard:view",
        "maintenance:close", "maintenance:create", "maintenance:manage", "maintenance:update", "maintenance:view",
        "notifications:view", "telematics:devices:view", "telematics:diagnostics:view", "telematics:gps:view",
        "telematics:sensors:view", "vehicles:view"
    ];

    private static readonly string[] ExpectedClosure =
    [
        "alerts:acknowledge", "alerts:close", "alerts:view", "compliance:view", "dashboard:view",
        "maintenance:close", "maintenance:create", "maintenance:manage", "maintenance:review", "maintenance:update",
        "maintenance:view", "notifications:view", "telematics:devices:view", "telematics:diagnostics:view",
        "telematics:gps:view", "telematics:sensors:view", "telemetry:alerts:read", "telemetry:devices:read",
        "telemetry:live_state:read", "vehicles:view"
    ];

    private const string CandidateHash = "377e41d694c20a8a49a6bbf334da4ef6f950e12edb04d286df40584fd255f2cc";
    private const string ClosureHash = "10579d2ac926f44bb553c8a0ed67c4704b18f49fbfa31c6882005f794ff797bd";

    [Fact]
    public async Task ExactCandidate_DualWritesRevokesSessionsAndAuditsInsideSignedTenantTransaction()
    {
        await using var fixture = await Fixture.Create();

        var result = await fixture.Update(Candidate, ["*"]);

        Assert.Equal(StatusCodes.Status200OK, Status(result));
        var (jsonStore, normalizedStore) = await fixture.DirectStores();
        Assert.Equal(Candidate, jsonStore);
        Assert.Equal(Candidate, normalizedStore);
        Assert.Equal(CandidateHash, Hash(jsonStore));

        var closure = EffectiveClosure(Candidate);
        Assert.Equal(ExpectedClosure, closure);
        Assert.Equal(ClosureHash, Hash(closure));

        Assert.Equal(0, await fixture.AffectedSessionCount());
        Assert.Equal(1, await fixture.UnrelatedSessionCount());
        Assert.Equal(5, await fixture.AssignedUserCount());

        var audit = await fixture.SingleRoleUpdatedAudit();
        Assert.Equal(fixture.ActorUserId, Convert.ToInt64(audit["actorUserId"]));
        Assert.Equal($"Company Admin:{fixture.ActorUserId}", audit["actorName"]);
        Assert.Equal("Role", audit["entityName"]);
        Assert.Equal(fixture.RoleId, Convert.ToInt64(audit["entityId"]));
        using var details = JsonDocument.Parse(audit["detailsJson"]!.ToString()!);
        Assert.True(details.RootElement.GetProperty("sessionsRevoked").GetBoolean());
        var payload = JsonSerializer.Deserialize<string[]>(details.RootElement.GetProperty("permissions").GetString()!)!;
        Assert.Equal(Candidate, payload);

        Assert.Equal("Synthetic Maintenance Manager", await fixture.RoleName());
        Assert.Equal(fixture.CompanyId, await fixture.RoleCompany());
    }

    [Fact]
    public async Task InvalidAndActorWithheldPermissions_LeaveStoresSessionsAndAuditUnchanged()
    {
        await using var fixture = await Fixture.Create();
        var before = await fixture.Snapshot();

        var unknown = await fixture.Update(["dashboard:view", "roles:delete:protected"], ["*"]);
        Assert.Equal(StatusCodes.Status400BadRequest, Status(unknown));
        Assert.Equal(before, await fixture.Snapshot());

        var withheld = await fixture.Update(["dashboard:view", "maintenance:view"], ["roles:update", "dashboard:view"]);
        Assert.Equal(StatusCodes.Status403Forbidden, Status(withheld));
        Assert.Equal(before, await fixture.Snapshot());
    }

    [Fact]
    public async Task NormalizedStoreFailure_RollsBackRoleStoresSessionsAndAudit()
    {
        await using var fixture = await Fixture.Create();
        var before = await fixture.Snapshot();
        await fixture.FailNormalizedInsert("maintenance:manage");

        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.Update(Candidate, ["*"]));

        Assert.Equal("P0001", error.SqlState);
        Assert.Contains("synthetic normalized-role failure", error.MessageText, StringComparison.Ordinal);
        Assert.Equal(before, await fixture.Snapshot());
    }

    [Fact]
    public async Task AuditFailure_RollsBackRoleStoresAndRestoresAllAffectedSessions()
    {
        await using var fixture = await Fixture.Create();
        var before = await fixture.Snapshot();
        await fixture.FailRoleUpdatedAudit();

        var error = await Assert.ThrowsAsync<PostgresException>(() => fixture.Update(Candidate, ["*"]));

        Assert.Equal("P0001", error.SqlState);
        Assert.Contains("synthetic role-audit failure", error.MessageText, StringComparison.Ordinal);
        Assert.Equal(before, await fixture.Snapshot());
    }

    private static int Status(IResult result)
        => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? StatusCodes.Status200OK;

    private static string Hash(IEnumerable<string> values)
    {
        var canonical = values.Select(PermissionPolicy.Canonicalize)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical))))
            .ToLowerInvariant();
    }

    private static string[] EffectiveClosure(IReadOnlyCollection<string> direct)
    {
        var field = typeof(PermissionPolicy).GetField("Implications", BindingFlags.Static | BindingFlags.NonPublic)!;
        var graph = Assert.IsType<Dictionary<string, HashSet<string>>>(field.GetValue(null));
        return direct.Select(PermissionPolicy.Canonicalize)
            .Concat(graph.Keys)
            .Concat(graph.Values.SelectMany(static values => values))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(permission => PermissionPolicy.Allows(direct, permission))
            .Select(PermissionPolicy.Canonicalize)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static permission => permission, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly string[] Preimage = ["dashboard:view", "reports:view"];

        private readonly Database _owner;
        private readonly Database _runtime;
        private readonly WebApplication _app;
        private readonly string _suffix;
        private readonly List<(string Trigger, string Function, string Table)> _failures = [];
        private readonly List<long> _assignedUsers = [];
        private long _branchId;

        public long CompanyId { get; private set; }
        public long RoleId { get; private set; }
        public long ActorUserId { get; private set; }

        private Fixture(Database owner, Database runtime, WebApplication app, string suffix)
        {
            _owner = owner;
            _runtime = runtime;
            _app = app;
            _suffix = suffix;
        }

        public static async Task<Fixture> Create()
        {
            Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB")),
                "An explicit disposable integration database is required; the TestDb fallback is not admissible.");

            var owner = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString);
            var appIdentity = new NpgsqlConnectionStringBuilder(TestDb.AppConnectionString);
            var systemIdentity = new NpgsqlConnectionStringBuilder(TestDb.SystemConnectionString);
            foreach (var target in new[] { owner, appIdentity, systemIdentity })
            {
                Assert.Contains(target.Host, new[] { "127.0.0.1", "localhost", "::1" });
                Assert.Equal(owner.Host, target.Host);
                Assert.Equal(owner.Port, target.Port);
                Assert.Equal(owner.Database, target.Database);
            }
            Assert.Equal("opstrax_app", appIdentity.Username);
            Assert.Equal("opstrax_system", systemIdentity.Username);
            Assert.DoesNotContain(owner.Username, new[] { "opstrax_app", "opstrax_system" });
            var githubActions = string.Equals(
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);
            Assert.True(githubActions || owner.Database?.StartsWith("opstrax_a02_", StringComparison.OrdinalIgnoreCase) == true,
                "Outside the hermetic GitHub Actions PostgreSQL service, the database name must start with opstrax_a02_.");

            var ownerDb = new Database(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = owner.ConnectionString,
                ["Rls:EnforceTenantContext"] = "false"
            }).Build());
            var accessor = new TenantScopeAccessor();
            var runtime = new Database(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Staging",
                ["Rls:EnforceTenantContext"] = "true",
                ["ConnectionStrings:DefaultConnection"] = appIdentity.ConnectionString,
                ["ConnectionStrings:SystemConnection"] = systemIdentity.ConnectionString,
                ["Rls:TenantTicketTtlSeconds"] = "120"
            }).Build(), accessor);
            await runtime.ValidateProductionIdentitiesAsync();

            var app = WebApplication.CreateBuilder().Build();
            app.MapOpsTraxEndpoints();
            var fixture = new Fixture(ownerDb, runtime, app, Guid.NewGuid().ToString("N"));
            try
            {
                await fixture.Initialize();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        private async Task Initialize()
        {
            CompanyId = await _owner.InsertAsync(
                "INSERT INTO companies(company_code,name,industry,status) VALUES(@code,@name,'Logistics','Active')",
                command =>
                {
                    command.Parameters.AddWithValue("code", $"A02-{_suffix[..16]}".ToUpperInvariant());
                    command.Parameters.AddWithValue("name", $"A-02 disposable rehearsal {_suffix[..8]}");
                });
            _branchId = await _owner.InsertAsync(
                "INSERT INTO branches(company_id,branch_code,name,status) VALUES(@company,@code,@name,'Active')",
                command =>
                {
                    command.Parameters.AddWithValue("company", CompanyId);
                    command.Parameters.AddWithValue("code", $"A02-B-{_suffix[..12]}".ToUpperInvariant());
                    command.Parameters.AddWithValue("name", "Synthetic A-02 branch");
                });
            RoleId = await _owner.InsertAsync(
                "INSERT INTO roles(company_id,name,permissions_json,is_system) VALUES(@company,'Synthetic Maintenance Manager',@permissions::jsonb,FALSE)",
                command =>
                {
                    command.Parameters.AddWithValue("company", CompanyId);
                    command.Parameters.AddWithValue("permissions", JsonSerializer.Serialize(Preimage));
                });
            foreach (var permission in Preimage)
            {
                await _owner.ExecuteAsync(
                    "INSERT INTO role_permissions(role_id,permission_key) VALUES(@role,@permission)",
                    command =>
                    {
                        command.Parameters.AddWithValue("role", RoleId);
                        command.Parameters.AddWithValue("permission", permission);
                    });
            }

            ActorUserId = await AddUser("A-02 Synthetic Administrator", "Company Admin", null, 0);
            for (var index = 0; index < 5; index++)
                _assignedUsers.Add(await AddUser($"A-02 Assigned User {index + 1}", "Synthetic Maintenance Manager", RoleId, index + 1));
        }

        private async Task<long> AddUser(string name, string roleName, long? roleId, int ordinal)
        {
            var userId = await _owner.InsertAsync(
                """
                INSERT INTO users(company_id,branch_id,role_id,full_name,email,role_name,status,permissions_json)
                VALUES(@company,@branch,@role,@name,@email,@roleName,'Active','[]'::jsonb)
                """,
                command =>
                {
                    command.Parameters.AddWithValue("company", CompanyId);
                    command.Parameters.AddWithValue("branch", _branchId);
                    command.Parameters.AddWithValue("role", (object?)roleId ?? DBNull.Value);
                    command.Parameters.AddWithValue("name", name);
                    command.Parameters.AddWithValue("email", $"a02-{ordinal}-{_suffix}@example.invalid");
                    command.Parameters.AddWithValue("roleName", roleName);
                });
            await _owner.ExecuteAsync(
                "INSERT INTO user_sessions(user_id,company_id,session_token,expires_at) VALUES(@user,@company,@token,NOW()+INTERVAL '1 hour')",
                command =>
                {
                    command.Parameters.AddWithValue("user", userId);
                    command.Parameters.AddWithValue("company", CompanyId);
                    command.Parameters.AddWithValue("token", $"a02-{ordinal}-{_suffix}");
                });
            return userId;
        }

        public Task<IResult> Update(IReadOnlyCollection<string> permissions, string[] actorPermissions)
            => _runtime.RunInTenantScopeAsync(CompanyId, async () =>
            {
                var identity = await _runtime.QuerySingleAsync(
                    "SELECT current_user AS role,opstrax_security.current_tenant_id() AS tenant");
                Assert.Equal("opstrax_app", identity!["role"]);
                Assert.Equal(CompanyId, Convert.ToInt64(identity["tenant"]));

                var http = new DefaultHttpContext();
                http.Items[EndpointMappings.AuthCompanyIdItemKey] = CompanyId;
                http.Items[EndpointMappings.AuthBranchIdItemKey] = _branchId;
                http.Items[EndpointMappings.AuthUserIdItemKey] = ActorUserId;
                http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
                http.Items[EndpointMappings.AuthPermissionsItemKey] = actorPermissions;
                var body = new Dictionary<string, object?>
                {
                    ["name"] = "Synthetic Maintenance Manager",
                    ["permissions"] = permissions.ToArray()
                };
                var handler = RegisteredRoleUpdate();
                var args = handler.Method.GetParameters().Select(parameter =>
                    parameter.ParameterType == typeof(HttpContext) ? (object)http :
                    parameter.ParameterType == typeof(long) ? RoleId :
                    parameter.ParameterType == typeof(Dictionary<string, object?>) ? body :
                    parameter.ParameterType == typeof(Database) ? _runtime :
                    parameter.ParameterType == typeof(AuditService) ? new AuditService(_runtime) :
                    parameter.ParameterType == typeof(CancellationToken) ? CancellationToken.None :
                    throw new InvalidOperationException($"Unexpected registered handler parameter {parameter.ParameterType}")).ToArray();
                try
                {
                    return await (Task<IResult>)handler.DynamicInvoke(args)!;
                }
                catch (TargetInvocationException error) when (error.InnerException is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                    throw;
                }
            });

        private Delegate RegisteredRoleUpdate()
        {
            const string path = "/api/admin/roles/{id:long}";
            var matches = new List<Delegate>();
            foreach (var source in ((IEndpointRouteBuilder)_app).DataSources)
            {
                if (source.GetType().GetField("_routeEntries", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(source) is not IEnumerable entries)
                    continue;
                foreach (var entry in entries)
                {
                    if (entry is not null && Member(entry, "RoutePattern") is RoutePattern pattern && pattern.RawText == path
                        && Member(entry, "RouteHandler") is Delegate handler
                        && handler.Method.GetParameters().Any(parameter => parameter.ParameterType == typeof(Dictionary<string, object?>)))
                        matches.Add(handler);
                }
            }
            var match = Assert.Single(matches);
            Assert.Equal(typeof(EndpointMappings).Assembly, match.Method.Module.Assembly);
            Assert.Equal("UpdateAdminRole", match.Method.Name);
            return match;
        }

        private static object? Member(object value, string name)
            => value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value)
               ?? value.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);

        public async Task<(string[] JsonStore, string[] NormalizedStore)> DirectStores()
        {
            var role = await _owner.QuerySingleAsync(
                "SELECT permissions_json::text AS permissions FROM roles WHERE id=@role AND company_id=@company",
                command =>
                {
                    command.Parameters.AddWithValue("role", RoleId);
                    command.Parameters.AddWithValue("company", CompanyId);
                });
            var json = JsonSerializer.Deserialize<string[]>(role!["permissions"]!.ToString()!)!
                .Select(PermissionPolicy.Canonicalize).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
            var rows = await _owner.QueryAsync(
                "SELECT permission_key FROM role_permissions WHERE role_id=@role ORDER BY permission_key",
                command => command.Parameters.AddWithValue("role", RoleId));
            var normalized = rows.Select(row => PermissionPolicy.Canonicalize(row["permissionKey"]!.ToString()!))
                .OrderBy(static value => value, StringComparer.Ordinal).ToArray();
            return (json, normalized);
        }

        public Task<long> AffectedSessionCount() => _owner.ScalarLongAsync(
            "SELECT COUNT(*) FROM user_sessions WHERE user_id=ANY(@users)",
            command => command.Parameters.AddWithValue("users", _assignedUsers.ToArray()));

        public Task<long> UnrelatedSessionCount() => _owner.ScalarLongAsync(
            "SELECT COUNT(*) FROM user_sessions WHERE user_id=@actor",
            command => command.Parameters.AddWithValue("actor", ActorUserId));

        public Task<long> AssignedUserCount() => _owner.ScalarLongAsync(
            "SELECT COUNT(*) FROM users WHERE company_id=@company AND role_id=@role",
            command =>
            {
                command.Parameters.AddWithValue("company", CompanyId);
                command.Parameters.AddWithValue("role", RoleId);
            });

        public async Task<Dictionary<string, object?>> SingleRoleUpdatedAudit()
        {
            var rows = await _owner.QueryAsync(
                """
                SELECT actor_user_id,actor_name,entity_name,entity_id,details_json::text AS details_json
                  FROM audit_logs
                 WHERE company_id=@company AND action_name='role.updated' AND entity_id=@role
                 ORDER BY id
                """,
                command =>
                {
                    command.Parameters.AddWithValue("company", CompanyId);
                    command.Parameters.AddWithValue("role", RoleId);
                });
            return Assert.Single(rows);
        }

        public async Task<string> RoleName()
        {
            var row = await _owner.QuerySingleAsync("SELECT name FROM roles WHERE id=@role",
                command => command.Parameters.AddWithValue("role", RoleId));
            return row!["name"]!.ToString()!;
        }

        public Task<long> RoleCompany() => _owner.ScalarLongAsync("SELECT company_id FROM roles WHERE id=@role",
            command => command.Parameters.AddWithValue("role", RoleId));

        public async Task<string> Snapshot()
        {
            var row = await _owner.QuerySingleAsync(
                """
                SELECT jsonb_build_object(
                    'role',(SELECT jsonb_build_object('name',name,'company_id',company_id,'permissions',permissions_json)
                              FROM roles WHERE id=@role),
                    'normalized',(SELECT COALESCE(jsonb_agg(permission_key ORDER BY permission_key),'[]'::jsonb)
                                    FROM role_permissions WHERE role_id=@role),
                    'users',(SELECT COALESCE(jsonb_agg(jsonb_build_object('id',id,'role_id',role_id,'branch_id',branch_id,'status',status) ORDER BY id),'[]'::jsonb)
                               FROM users WHERE company_id=@company),
                    'sessions',(SELECT COALESCE(jsonb_agg(jsonb_build_object('user_id',user_id,'token',session_token,'expires_at',expires_at) ORDER BY id),'[]'::jsonb)
                                  FROM user_sessions WHERE company_id=@company),
                    'audit',(SELECT COALESCE(jsonb_agg(jsonb_build_object('action',action_name,'entity',entity_name,'entity_id',entity_id,'details',details_json) ORDER BY id),'[]'::jsonb)
                               FROM audit_logs WHERE company_id=@company)
                )::text AS payload
                """,
                command =>
                {
                    command.Parameters.AddWithValue("role", RoleId);
                    command.Parameters.AddWithValue("company", CompanyId);
                });
            return row!["payload"]!.ToString()!;
        }

        public Task FailNormalizedInsert(string permission)
            => InstallFailure("role_permissions", "rp", $"NEW.role_id={RoleId} AND NEW.permission_key={Literal(permission)}",
                "synthetic normalized-role failure");

        public Task FailRoleUpdatedAudit()
            => InstallFailure("audit_logs", "audit", $"NEW.company_id={CompanyId} AND NEW.action_name='role.updated'",
                "synthetic role-audit failure");

        private async Task InstallFailure(string table, string label, string predicate, string message)
        {
            var function = $"a02_{label}_fn_{_suffix[..12]}";
            var trigger = $"a02_{label}_tr_{_suffix[..12]}";
            await _owner.ExecuteAsync($$"""
                CREATE FUNCTION public.{{function}}() RETURNS trigger LANGUAGE plpgsql AS $a02$
                BEGIN
                    IF {{predicate}} THEN
                        RAISE EXCEPTION '{{message}}' USING ERRCODE='P0001';
                    END IF;
                    RETURN NEW;
                END
                $a02$;
                CREATE TRIGGER {{trigger}} BEFORE INSERT ON public.{{table}}
                FOR EACH ROW EXECUTE FUNCTION public.{{function}}();
                """);
            _failures.Add((trigger, function, table));
        }

        private static string Literal(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

        public async ValueTask DisposeAsync()
        {
            foreach (var failure in _failures.AsEnumerable().Reverse())
            {
                try
                {
                    await _owner.ExecuteAsync($"DROP TRIGGER IF EXISTS {failure.Trigger} ON public.{failure.Table}; DROP FUNCTION IF EXISTS public.{failure.Function}();");
                }
                catch { /* continue exact fixture cleanup */ }
            }
            if (CompanyId > 0)
            {
                await _owner.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@company", command => command.Parameters.AddWithValue("company", CompanyId));
                await _owner.ExecuteAsync("DELETE FROM user_sessions WHERE company_id=@company", command => command.Parameters.AddWithValue("company", CompanyId));
                await _owner.ExecuteAsync("DELETE FROM users WHERE company_id=@company", command => command.Parameters.AddWithValue("company", CompanyId));
                if (RoleId > 0)
                {
                    await _owner.ExecuteAsync("DELETE FROM role_permissions WHERE role_id=@role", command => command.Parameters.AddWithValue("role", RoleId));
                    await _owner.ExecuteAsync("DELETE FROM roles WHERE id=@role", command => command.Parameters.AddWithValue("role", RoleId));
                }
                if (_branchId > 0)
                    await _owner.ExecuteAsync("DELETE FROM branches WHERE id=@branch", command => command.Parameters.AddWithValue("branch", _branchId));
                await _owner.ExecuteAsync("DELETE FROM companies WHERE id=@company", command => command.Parameters.AddWithValue("company", CompanyId));
            }
            await _app.DisposeAsync();
        }
    }
}
