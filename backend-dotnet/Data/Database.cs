using Npgsql;

namespace Opstrax.Api.Data;

// Carries the current request's tenant transaction across the async call chain.
// AsyncLocal is per-execution-context, so it is safe to hold on a singleton and
// never leaks between concurrent requests. The value MUST be assigned from the
// caller's own (non-nested) frame — e.g. the request middleware — so it flows
// into the awaited continuation; see RlsTenantMiddleware.
public sealed class TenantScopeAccessor
{
    private readonly AsyncLocal<TenantScope?> _current = new();
    public TenantScope? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

// A request-scoped connection + transaction whose FIRST statement set the tenant
// GUC via set_config(..., is_local:=true) (i.e. SET LOCAL semantics). Because the
// setting is transaction-local it is discarded at COMMIT/ROLLBACK and can never
// leak onto a pooled connection reused by another tenant's request.
public sealed class TenantScope : IAsyncDisposable
{
    internal NpgsqlConnection Connection { get; }
    internal NpgsqlTransaction Transaction { get; }
    // Factory-owned identity, never populated from an HTTP body or a system scope.
    internal long? CompanyId { get; }
    private bool _completed;
    private readonly Func<CancellationToken, Task>? _refreshTenantTicket;
    private readonly TimeSpan _ticketRefreshInterval;
    private long _refreshAfterTimestamp;

    // Serializes command execution on this scope's single shared connection. Under RLS
    // enforcement every query in a request runs on ONE connection/transaction; code that
    // fans out queries concurrently (e.g. Task.WhenAll) would otherwise throw
    // "a command is already in progress". This gate makes such calls queue safely.
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal TenantScope(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Func<CancellationToken, Task>? refreshTenantTicket = null,
        TimeSpan? ticketRefreshInterval = null,
        long? companyId = null)
    {
        Connection = connection;
        Transaction = transaction;
        CompanyId = companyId;
        _refreshTenantTicket = refreshTenantTicket;
        _ticketRefreshInterval = ticketRefreshInterval ?? TimeSpan.Zero;
        _refreshAfterTimestamp = NextRefreshTimestamp(_ticketRefreshInterval);
    }

    // Called only while Gate is held. Renewal reissues the exact same tenant/PID/txid
    // binding and replaces the transaction-local ticket before it expires. A failed
    // renewal is propagated so the next data statement cannot run with stale authority.
    internal async Task RefreshTenantTicketIfNeededAsync(CancellationToken ct)
    {
        if (_refreshTenantTicket is null ||
            System.Diagnostics.Stopwatch.GetTimestamp() < Volatile.Read(ref _refreshAfterTimestamp))
            return;

        await _refreshTenantTicket(ct);
        Volatile.Write(ref _refreshAfterTimestamp, NextRefreshTimestamp(_ticketRefreshInterval));
    }

    private static long NextRefreshTimestamp(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero) return long.MaxValue;
        var delta = (long)(interval.TotalSeconds * System.Diagnostics.Stopwatch.Frequency);
        return checked(System.Diagnostics.Stopwatch.GetTimestamp() + Math.Max(1L, delta));
    }

    // Commit the request transaction (persists any writes made within it).
    public async Task CompleteAsync(CancellationToken ct = default)
    {
        if (_completed) return;
        await Transaction.CommitAsync(ct);
        _completed = true;
    }

    // On any path that did not Complete (exception, error), roll back — then always
    // release the physical connection back to the pool with no residual GUC.
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_completed) await Transaction.RollbackAsync();
        }
        catch { /* connection already broken — nothing to roll back */ }
        finally
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
            Gate.Dispose();
        }
    }
}

public sealed class Database(IConfiguration configuration, TenantScopeAccessor? scopes = null)
{
    // Credentials are environment-only — never hard-coded in appsettings.json.
    // Protected-environment resolution is explicit: ConnectionStrings:DefaultConnection or
    // PG_CONNECTION_APP. The legacy PG_CONNECTION fallback is non-production only.
    private readonly string _connectionString =
        ResolveAppConnection(configuration)
        ?? throw new InvalidOperationException(
            "Application database connection string is not configured. Set ConnectionStrings__DefaultConnection or PG_CONNECTION_APP.");

    private readonly bool _protectedRls =
        configuration.GetValue<bool>("Rls:EnforceTenantContext") &&
        IsProtectedEnvironment(configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"] ?? configuration["Environment"]);

    // Cross-tenant bootstrap, platform, public-token and background work uses a
    // physically separate pool and the narrowly-granted opstrax_system login. In
    // Production/Staging+RLS this value is mandatory and NEVER falls back to the app pool.
    // Non-production retains the one-identity fallback so local schema tools and the
    // existing owner-backed integration tests keep working without production secrets.
    private readonly string? _systemConnectionString = ResolveSystemConnection(configuration);

    // Optional read-replica endpoint (Neon read replica / hot standby). When set,
    // read-only work can be routed here to offload the primary and to keep serving
    // reads if the primary is degraded. Falls back to the primary automatically when
    // unset or unreachable — a graceful read-path bypass, never a hard dependency.
    private readonly string? _replicaConnectionString =
        Coalesce(
            configuration.GetConnectionString("ReadReplica"),
            Environment.GetEnvironmentVariable("PG_CONNECTION_REPLICA"));

    /// <summary>True when a read replica is configured (surfaced in health/reliability).</summary>
    public bool HasReadReplica => !string.IsNullOrWhiteSpace(_replicaConnectionString);

    // Optional so tests / schema services can still do `new Database(config)` — in
    // that case there is never an ambient scope and every call uses its own
    // connection (unchanged pre-RLS behaviour). DI injects the shared singleton.
    private readonly TenantScopeAccessor _scopes = scopes ?? new TenantScopeAccessor();

    // Mirrors the request-pipeline gate (Program.cs `Rls:EnforceTenantContext`). When
    // false, RunInSystemScopeAsync is a pass-through (unchanged pre-RLS behaviour and
    // why the local app + tests are unaffected). When true, off-pipeline callers
    // (background workers, SSE) MUST wrap their DB work in a scope or the restricted
    // `opstrax_app` role returns 0 rows (fail-closed) — see BeginSystemScopeAsync.
    public bool RlsEnforced { get; } = configuration.GetValue<bool>("Rls:EnforceTenantContext");
    internal bool HasAmbientTransaction => _scopes.Current is not null;
    private readonly int _tenantTicketTtlSeconds = ResolveTenantTicketTtl(configuration);

    private static string? Coalesce(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? ResolveAppConnection(IConfiguration config)
    {
        var explicitApp = Coalesce(
            config.GetConnectionString("DefaultConnection"),
            config["PG_CONNECTION_APP"],
            Environment.GetEnvironmentVariable("PG_CONNECTION_APP"));
        var protectedRls = config.GetValue<bool>("Rls:EnforceTenantContext") &&
            IsProtectedEnvironment(config["ASPNETCORE_ENVIRONMENT"] ?? config["DOTNET_ENVIRONMENT"] ?? config["Environment"]);
        return protectedRls
            ? explicitApp
            : Coalesce(explicitApp, config["PG_CONNECTION"], Environment.GetEnvironmentVariable("PG_CONNECTION"));
    }

    private static string? ResolveSystemConnection(IConfiguration config)
    {
        var explicitSystem = Coalesce(
            config.GetConnectionString("SystemConnection"),
            config["PG_CONNECTION_SYSTEM"],
            Environment.GetEnvironmentVariable("PG_CONNECTION_SYSTEM"));
        var protectedRls = config.GetValue<bool>("Rls:EnforceTenantContext") &&
            IsProtectedEnvironment(config["ASPNETCORE_ENVIRONMENT"] ?? config["DOTNET_ENVIRONMENT"] ?? config["Environment"]);
        if (protectedRls && string.IsNullOrWhiteSpace(explicitSystem))
            throw new InvalidOperationException(
                "System database connection string is required in Production or Staging with RLS. Set ConnectionStrings__SystemConnection or PG_CONNECTION_SYSTEM.");
        return explicitSystem;
    }

    private static bool IsProtectedEnvironment(string? environment) =>
        string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environment, "Staging", StringComparison.OrdinalIgnoreCase);

    private static int ResolveTenantTicketTtl(IConfiguration config)
    {
        var value = config.GetValue<int?>("Rls:TenantTicketTtlSeconds") ?? 120;
        if (config.GetValue<bool>("Rls:EnforceTenantContext") && value is < 5 or > 300)
            throw new InvalidOperationException("Rls:TenantTicketTtlSeconds must be between 5 and 300 seconds.");
        return value;
    }

    // ── DB metrics hook (observability) ─────────────────────────────────────────
    // Optional sink wired once at startup (Program.cs) so every query records its
    // latency + success/failure into ApiMetricsService, feeding the DB-latency and
    // db-connection-failure metrics/alerts. Static so the many `new Database(config)`
    // call sites (tests, schema services) are unaffected and never NRE.
    public static Action<double, bool>? MetricsSink { get; set; }

    private static void RecordDb(System.Diagnostics.Stopwatch sw, bool failed)
    {
        var sink = MetricsSink;
        if (sink is null) return;
        try { sw.Stop(); sink(sw.Elapsed.TotalMilliseconds, failed); } catch { /* never break a query */ }
    }

    // Runs off-pipeline work under the separately authenticated system scope so
    // cross-tenant background processing can reach its narrowly-granted tables. No-op
    // wrapper when enforcement is off. The
    // ambient scope is assigned from THIS frame so it flows into the awaited body, then
    // always cleared — never leaks onto a pooled connection.
    public async Task RunInSystemScopeAsync(Func<Task> body, CancellationToken ct = default)
    {
        if (!RlsEnforced) { await body(); return; }
        var priorScope = _scopes.Current;
        await using var sys = await BeginSystemScopeAsync(ct);
        _scopes.Current = sys;
        try { await body(); await sys.CompleteAsync(ct); }
        finally { _scopes.Current = priorScope; }
    }

    public async Task<T> RunInSystemScopeAsync<T>(Func<Task<T>> body, CancellationToken ct = default)
    {
        if (!RlsEnforced) return await body();
        var priorScope = _scopes.Current;
        await using var sys = await BeginSystemScopeAsync(ct);
        _scopes.Current = sys;
        try
        {
            var result = await body();
            await sys.CompleteAsync(ct);
            return result;
        }
        finally { _scopes.Current = priorScope; }
    }

    // Runs a single short-lived tenant-scoped read/unit of work and returns its result.
    // Used by the SSE stream, which must NOT hold one request-length transaction open for
    // the connection's whole lifetime: instead each 3s tick opens a scope, reads, commits,
    // and releases the connection. No-op wrapper (fresh per-query connection) when
    // enforcement is off. The ambient scope is set from THIS frame and always cleared.
    public async Task<T> RunInTenantScopeAsync<T>(long companyId, Func<Task<T>> body, CancellationToken ct = default)
    {
        if (!RlsEnforced) return await body();
        var priorScope = _scopes.Current;
        await using var scope = await BeginTenantScopeAsync(companyId, ct);
        _scopes.Current = scope;
        try { var result = await body(); await scope.CompleteAsync(ct); return result; }
        finally { _scopes.Current = priorScope; }
    }

    // Runs a mutation as one transaction even when RLS is disabled locally. Request
    // handlers normally already have an ambient tenant transaction when RLS is on;
    // in that case reuse it rather than nesting transactions. Core assignment writes
    // use this to keep reciprocal links, history, jobs, and dispatch rows atomic.
    public async Task<T> RunInTenantTransactionAsync<T>(long companyId, Func<Task<T>> body, CancellationToken ct = default)
    {
        if (_scopes.Current is not null) return await body();
        await using var scope = await BeginTenantScopeAsync(companyId, ct);
        _scopes.Current = scope;
        try
        {
            var result = await body();
            await scope.CompleteAsync(ct);
            return result;
        }
        finally { _scopes.Current = null; }
    }

    // Canonical document mutations need a committed business result before the HTTP
    // result is returned. Suspend (never complete/dispose) the request's outer scope.
    // This uses the SAME restricted application pool and signed tenant authority.
    // It costs a second pooled connection while an outer request scope is held.
    public async Task<T> RunInDocumentTransactionAsync<T>(long companyId, Func<Task<T>> body,
        Func<CancellationToken, Task>? afterConfirmedRollback = null, CancellationToken ct = default)
    {
        var priorScope = _scopes.Current;
        if (companyId <= 0 || (RlsEnforced && (priorScope is null || priorScope.CompanyId != companyId)))
            throw new InvalidOperationException("Document mutation requires the matching authenticated tenant scope.");
        await using var scope = await BeginTenantScopeAsync(companyId, ct);
        _scopes.Current = scope;
        var commitAttempted = false;
        try
        {
            var result = await body();
            commitAttempted = true;
            await scope.CompleteAsync(ct);
            return result;
        }
        catch (Exception failure)
        {
            // Commit may have reached PostgreSQL even if its acknowledgment was lost.
            // Never compensate an object after attempting it, including on cancellation.
            if (commitAttempted)
                throw new DocumentTransactionUncertainException("Document commit outcome is uncertain. Reload and reconcile before retrying.", failure);
            using var rollbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await scope.Transaction.RollbackAsync(rollbackTimeout.Token); }
            catch (Exception rollbackFailure)
            {
                throw new DocumentTransactionUncertainException("Document rollback was not confirmed. Reload and reconcile before retrying.", rollbackFailure);
            }
            if (afterConfirmedRollback is not null)
            {
                // This callback is object-storage compensation only. The inner DB
                // transaction is rolled back; callers must not execute DB commands here.
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await afterConfirmedRollback(cleanupTimeout.Token); }
                catch (Exception cleanupFailure)
                {
                    throw new DocumentTransactionUncertainException("Document writes rolled back, but uploaded-file cleanup needs reconciliation.", cleanupFailure);
                }
            }
            throw;
        }
        finally { _scopes.Current = priorScope; }
    }

    // Runs a cross-tenant/system mutation as one transaction even when RLS is disabled.
    // This is intentionally distinct from RunInSystemScopeAsync, whose non-RLS path is
    // a pass-through for background reads. Provisioning workflows such as demo-tenant
    // creation must be all-or-nothing: a mid-seed exception cannot leave a company row
    // that makes the next idempotent retry incorrectly report "already seeded".
    public async Task<T> RunInSystemTransactionAsync<T>(Func<Task<T>> body, CancellationToken ct = default)
    {
        // A dev-seed request can already have an authenticated tenant transaction.
        // Suspend (do not complete/dispose) that ambient scope while the independent
        // platform transaction runs, then restore it for the request middleware.
        var priorScope = _scopes.Current;
        await using var scope = await BeginSystemScopeAsync(ct);
        _scopes.Current = scope;
        try
        {
            var result = await body();
            await scope.CompleteAsync(ct);
            return result;
        }
        finally { _scopes.Current = priorScope; }
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
        => await OpenWithRetryAsync(_connectionString, ct);

    /// <summary>
    /// Opens the isolated system pool. Production/Staging+RLS never aliases or falls back to
    /// the application identity; local/test environments may use the app connection.
    /// </summary>
    public async Task<NpgsqlConnection> OpenSystemAsync(CancellationToken ct = default)
    {
        if (_protectedRls && string.IsNullOrWhiteSpace(_systemConnectionString))
            throw new InvalidOperationException("System database identity is unavailable.");
        return await OpenWithRetryAsync(_systemConnectionString ?? _connectionString, ct);
    }

    private static async Task<NpgsqlConnection> OpenWithRetryAsync(string connectionString, CancellationToken ct)
    {
        // Retry transient open failures with backoff. Neon's serverless pooler can
        // cold-start (scale-to-zero) or drop idle connections, so the first attempt
        // after an idle period may transiently fail — retrying avoids surfacing a 500.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var connection = new NpgsqlConnection(connectionString);
            try
            {
                await connection.OpenAsync(ct);
                return connection;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex) && !ct.IsCancellationRequested)
            {
                await connection.DisposeAsync();
                await Task.Delay(200 * attempt, ct); // 200ms, then 400ms
            }
        }
    }

    // Opens a connection for READ-ONLY work, preferring the read replica when one is
    // configured. If the replica open fails (replica degraded/unreachable), it FALLS
    // BACK to the primary — reads keep serving through a replica outage. Only for
    // queries that never write and are NOT inside an ambient tenant transaction.
    public async Task<NpgsqlConnection> OpenReadAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_replicaConnectionString))
            return await OpenAsync(ct);

        try
        {
            var replica = new NpgsqlConnection(_replicaConnectionString);
            await replica.OpenAsync(ct);
            return replica;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // Replica down → serve the read from the primary. Recorded as a DB failure
            // signal so the Reliability Center can surface replica degradation.
            MetricsSink?.Invoke(0, true);
            return await OpenAsync(ct);
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        NpgsqlException npg => npg.IsTransient,
        TimeoutException => true,
        _ => false,
    };

    /// <summary>
    /// Proves the two Production runtime pools resolve to the exact, non-privileged
    /// logins expected by Stage58. Configuration-string labels are not trusted: the
    /// server-reported current_user and role attributes are authoritative.
    /// </summary>
    public async Task ValidateProductionIdentitiesAsync(CancellationToken ct = default)
    {
        await using var app = await OpenAsync(ct);
        await using var system = await OpenSystemAsync(ct);
        var appIdentity = await ReadIdentityAsync(app, ct);
        var systemIdentity = await ReadIdentityAsync(system, ct);

        ValidateIdentity(appIdentity, "opstrax_app", "application");
        ValidateIdentity(systemIdentity, "opstrax_system", "system");
        if (string.Equals(appIdentity.RoleName, systemIdentity.RoleName, StringComparison.Ordinal) ||
            appIdentity.BackendPid == systemIdentity.BackendPid)
            throw new InvalidOperationException("Application and system database identities are not isolated.");

        // Prove both identities terminate at the same Stage58 trust domain. Matching
        // role names on two different databases is not enough: the system pool must
        // mint a ticket the app pool can verify for this exact backend transaction.
        await using (var tx = await app.BeginTransactionAsync(ct))
        {
            int pid;
            long txid;
            await using (var binding = new NpgsqlCommand(
                "SELECT pg_backend_pid(), txid_current()::bigint", app, tx))
            await using (var reader = await binding.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new InvalidOperationException("Database ticket bridge binding failed.");
                pid = reader.GetInt32(0);
                txid = reader.GetInt64(1);
            }

            const long probeTenant = long.MaxValue;
            var ticket = await IssueTenantTicketAsync(system, probeTenant, pid, txid, _tenantTicketTtlSeconds, ct);
            await using (var install = new NpgsqlCommand(
                "SELECT set_config('app.tenant_ticket',@ticket,true)", app, tx))
            {
                install.Parameters.AddWithValue("@ticket", ticket);
                await install.ExecuteNonQueryAsync(ct);
            }
            await using (var verify = new NpgsqlCommand(
                "SELECT opstrax_security.current_tenant_id()", app, tx))
            {
                var verified = await verify.ExecuteScalarAsync(ct);
                if (verified is null or DBNull || Convert.ToInt64(verified) != probeTenant)
                    throw new InvalidOperationException("Application/system database ticket bridge could not be proven.");
            }
            await tx.RollbackAsync(ct);
        }

        if (!string.IsNullOrWhiteSpace(_replicaConnectionString))
        {
            await using var replica = await OpenWithRetryAsync(_replicaConnectionString, ct);
            ValidateIdentity(await ReadIdentityAsync(replica, ct), "opstrax_app", "application replica");
        }
    }

    private sealed record RuntimeIdentity(
        string RoleName, int BackendPid, bool CanLogin, bool IsSuper, bool BypassRls,
        bool CanCreateDb, bool CanCreateRole, bool InheritsRoles, bool CanReplicate,
        int MembershipCount, int GrantedToRoleCount, int OwnedDatabaseCount,
        int OwnedSchemaCount, int OwnedRelationCount, int OwnedFunctionCount, int OwnedTypeCount,
        bool DbConnect, bool DbCreate, bool DbTemporary,
        bool SchemaUsage, bool SchemaCreate, bool CanIssueTicket, bool CanVerifyTicket);

    private static async Task<RuntimeIdentity> ReadIdentityAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            @"SELECT current_user, pg_backend_pid(), r.rolcanlogin, r.rolsuper,
                     r.rolbypassrls, r.rolcreatedb, r.rolcreaterole, r.rolinherit, r.rolreplication,
                     (SELECT COUNT(*)::int FROM pg_auth_members m WHERE m.member=r.oid),
                     -- Managed Postgres (Neon/RDS/Cloud SQL) auto-grants the database owner
                     -- ADMIN membership in every role created under it, recorded with the
                     -- provider's internal superuser as grantor, so the customer-visible owner
                     -- CANNOT revoke it (REVOKE is a no-op warning). Counting it here made this
                     -- proof unsatisfiable on every managed provider. The database owner already
                     -- holds unrestricted access, so that single edge grants it nothing new —
                     -- every OTHER principal still fails the proof.
                     (SELECT COUNT(*)::int FROM pg_auth_members m WHERE m.roleid=r.oid
                        AND m.member<>(SELECT d.datdba FROM pg_database d WHERE d.datname=current_database())),
                     (SELECT COUNT(*)::int FROM pg_database d WHERE d.datdba=r.oid),
                     (SELECT COUNT(*)::int FROM pg_namespace n WHERE n.nspowner=r.oid),
                     (SELECT COUNT(*)::int FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                       WHERE c.relowner=r.oid AND n.nspname NOT LIKE 'pg\_%' ESCAPE '\' AND n.nspname<>'information_schema'),
                     (SELECT COUNT(*)::int FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
                       WHERE p.proowner=r.oid AND n.nspname NOT LIKE 'pg\_%' ESCAPE '\' AND n.nspname<>'information_schema'),
                     (SELECT COUNT(*)::int FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace
                       WHERE t.typowner=r.oid AND n.nspname NOT LIKE 'pg\_%' ESCAPE '\' AND n.nspname<>'information_schema'),
                     has_database_privilege(current_user,current_database(),'CONNECT'),
                     has_database_privilege(current_user,current_database(),'CREATE'),
                     has_database_privilege(current_user,current_database(),'TEMPORARY'),
                     has_schema_privilege(current_user,'public','USAGE'),
                     has_schema_privilege(current_user,'public','CREATE'),
                     has_function_privilege(current_user,
                       'opstrax_security.issue_tenant_ticket(bigint,integer,bigint,integer)','EXECUTE'),
                     has_function_privilege(current_user,
                       'opstrax_security.current_tenant_id()','EXECUTE')
                FROM pg_roles r WHERE r.rolname=current_user", connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Database runtime identity could not be proven.");
        return new RuntimeIdentity(
            reader.GetString(0), reader.GetInt32(1), reader.GetBoolean(2), reader.GetBoolean(3),
            reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6), reader.GetBoolean(7),
            reader.GetBoolean(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11),
            reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15),
            reader.GetBoolean(16), reader.GetBoolean(17), reader.GetBoolean(18), reader.GetBoolean(19),
            reader.GetBoolean(20), reader.GetBoolean(21), reader.GetBoolean(22));
    }

    private static void ValidateIdentity(RuntimeIdentity identity, string expectedRole, string lane)
    {
        if (!string.Equals(identity.RoleName, expectedRole, StringComparison.Ordinal) ||
            !identity.CanLogin || identity.IsSuper || identity.BypassRls || identity.CanCreateDb ||
            identity.CanCreateRole || identity.InheritsRoles || identity.CanReplicate ||
            identity.MembershipCount != 0 || identity.GrantedToRoleCount != 0 ||
            identity.OwnedDatabaseCount != 0 || identity.OwnedSchemaCount != 0 ||
            identity.OwnedRelationCount != 0 || identity.OwnedFunctionCount != 0 ||
            identity.OwnedTypeCount != 0 ||
            !identity.DbConnect || identity.DbCreate ||
            identity.DbTemporary || !identity.SchemaUsage || identity.SchemaCreate ||
            !identity.CanVerifyTicket ||
            (lane.StartsWith("application", StringComparison.Ordinal)
                ? identity.CanIssueTicket
                : !identity.CanIssueTicket))
            throw new InvalidOperationException(
                $"The {lane} database identity is not the exact restricted {expectedRole} role.");
    }

    // ── Request-scoped tenant context (RLS activation, Option A1) ──────────────────
    // Opens an app connection+transaction, binds its PID+txid, obtains a short-lived
    // DB-signed ticket through opstrax_system, then installs that opaque ticket with
    // SET LOCAL semantics. A copied or fabricated tenant id cannot authorize RLS.
    public async Task<TenantScope> BeginTenantScopeAsync(long companyId, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        NpgsqlTransaction? tx = null;
        try
        {
            tx = await connection.BeginTransactionAsync(ct);
            if (!RlsEnforced)
            {
                // Legacy local/test posture only. Production is required to enable RLS
                // and therefore always takes the non-forgeable ticket path below.
                await using var legacy = new NpgsqlCommand(
                    "SELECT set_config('app.current_tenant_id', @cid, true)", connection, tx);
                legacy.Parameters.AddWithValue("@cid", companyId.ToString());
                await legacy.ExecuteNonQueryAsync(ct);
                return new TenantScope(connection, tx, companyId: companyId);
            }

            int backendPid;
            long transactionId;
            await using (var binding = new NpgsqlCommand(
                "SELECT pg_backend_pid(), txid_current()::bigint", connection, tx))
            await using (var reader = await binding.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new InvalidOperationException("Could not bind the tenant transaction to its database backend.");
                backendPid = reader.GetInt32(0);
                transactionId = reader.GetInt64(1);
            }

            string ticket;
            await using (var systemConnection = await OpenSystemAsync(ct))
            {
                ticket = await IssueTenantTicketAsync(
                    systemConnection, companyId, backendPid, transactionId, _tenantTicketTtlSeconds, ct);
            }

            if (string.IsNullOrWhiteSpace(ticket))
                throw new InvalidOperationException("The database did not issue a tenant transaction ticket.");

            await using (var install = new NpgsqlCommand(
                "SELECT set_config('app.tenant_ticket', @ticket, true)", connection, tx))
            {
                install.Parameters.AddWithValue("@ticket", ticket);
                await install.ExecuteNonQueryAsync(ct);
            }

            var refreshInterval = TimeSpan.FromSeconds(Math.Max(1, _tenantTicketTtlSeconds / 2));
            return new TenantScope(
                connection,
                tx,
                async refreshCt =>
                {
                    string renewedTicket;
                    await using (var systemConnection = await OpenSystemAsync(refreshCt))
                    {
                        renewedTicket = await IssueTenantTicketAsync(
                            systemConnection,
                            companyId,
                            backendPid,
                            transactionId,
                            _tenantTicketTtlSeconds,
                            refreshCt);
                    }

                    if (string.IsNullOrWhiteSpace(renewedTicket))
                        throw new InvalidOperationException("The database did not renew the tenant transaction ticket.");

                    await using var renew = new NpgsqlCommand(
                        "SELECT set_config('app.tenant_ticket', @ticket, true)", connection, tx);
                    renew.Parameters.AddWithValue("@ticket", renewedTicket);
                    await renew.ExecuteNonQueryAsync(refreshCt);
                },
                refreshInterval, companyId);
        }
        catch
        {
            if (tx is not null)
            {
                try { await tx.RollbackAsync(ct); } catch { }
                await tx.DisposeAsync();
            }
            await connection.DisposeAsync();
            throw;
        }
    }

    // System scope uses the distinct opstrax_system pool. It never sets a bypass GUC;
    // Stage58 grants the system control-plane policy directly to this exact role.
    public async Task<TenantScope> BeginSystemScopeAsync(CancellationToken ct = default)
    {
        var connection = await OpenSystemAsync(ct);
        var tx = await connection.BeginTransactionAsync(ct);
        return new TenantScope(connection, tx);
    }

    private static async Task<string> IssueTenantTicketAsync(
        NpgsqlConnection systemConnection,
        long companyId,
        int backendPid,
        long transactionId,
        int ttlSeconds,
        CancellationToken ct)
    {
        await using var issue = new NpgsqlCommand(
            "SELECT opstrax_security.issue_tenant_ticket(@tenant_id,@backend_pid,@txid,@ttl_seconds)",
            systemConnection);
        issue.Parameters.AddWithValue("@tenant_id", companyId);
        issue.Parameters.AddWithValue("@backend_pid", backendPid);
        issue.Parameters.AddWithValue("@txid", transactionId);
        issue.Parameters.AddWithValue("@ttl_seconds", ttlSeconds);
        return (await issue.ExecuteScalarAsync(ct))?.ToString() ?? string.Empty;
    }

    // Returns the connection+transaction to use for a query: the ambient request
    // scope if one is active (shared, not disposed here), otherwise a fresh
    // connection owned by the caller. When an ambient scope is active, its Gate is
    // acquired here and MUST be released via ReleaseAsync in the caller's finally —
    // this serializes concurrent commands on the single shared connection.
    private async Task<(NpgsqlConnection connection, NpgsqlTransaction? tx, bool owns, TenantScope? scope)> AcquireAsync(CancellationToken ct)
    {
        var scope = _scopes.Current;
        if (scope is not null)
        {
            await scope.Gate.WaitAsync(ct);
            try
            {
                await scope.RefreshTenantTicketIfNeededAsync(ct);
                return (scope.Connection, scope.Transaction, false, scope);
            }
            catch
            {
                ReleaseScope(scope);
                throw;
            }
        }
        var connection = await OpenAsync(ct);
        return (connection, null, true, null);
    }

    // Release the shared-scope gate (no-op when not in a scope). Guarded against
    // over-release in case the gate was already disposed on scope teardown.
    private static void ReleaseScope(TenantScope? scope)
    {
        if (scope is null) return;
        try { scope.Gate.Release(); } catch { /* disposed on scope end — ignore */ }
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var failed = false;
        var (connection, tx, owns, scope) = await AcquireAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            bind?.Invoke(command);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[ToCamel(reader.GetName(i))] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }
            return rows;
        }
        catch { failed = true; throw; }
        finally
        {
            RecordDb(sw, failed);
            ReleaseScope(scope);
            if (owns) await connection.DisposeAsync();
        }
    }

    public async Task<Dictionary<string, object?>?> QuerySingleAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
        => (await QueryAsync(sql, bind, ct)).FirstOrDefault();

    public async Task<Dictionary<string, object?>?> QuerySingleInSystemScopeAsync(
        string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var scope = await BeginSystemScopeAsync(ct);
        Dictionary<string, object?>? row = null;
        await using (var command = new NpgsqlCommand(sql, scope.Connection, scope.Transaction))
        {
            bind?.Invoke(command);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                    row[ToCamel(reader.GetName(i))] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
        }

        await scope.CompleteAsync(ct);
        return row;
    }

    public async Task<long> ScalarLongAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var failed = false;
        var (connection, tx, owns, scope) = await AcquireAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            bind?.Invoke(command);
            var value = await command.ExecuteScalarAsync(ct);
            return value is null or DBNull ? 0 : Convert.ToInt64(value);
        }
        catch { failed = true; throw; }
        finally
        {
            RecordDb(sw, failed);
            ReleaseScope(scope);
            if (owns) await connection.DisposeAsync();
        }
    }

    public async Task<long> ScalarLongInSystemScopeAsync(
        string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var scope = await BeginSystemScopeAsync(ct);
        object? value;
        await using (var command = new NpgsqlCommand(sql, scope.Connection, scope.Transaction))
        {
            bind?.Invoke(command);
            value = await command.ExecuteScalarAsync(ct);
        }
        await scope.CompleteAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    public async Task<decimal?> ScalarDecimalAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        var (connection, tx, owns, scope) = await AcquireAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            bind?.Invoke(command);
            var value = await command.ExecuteScalarAsync(ct);
            return value is null or DBNull ? null : Convert.ToDecimal(value);
        }
        finally
        {
            ReleaseScope(scope);
            if (owns) await connection.DisposeAsync();
        }
    }

    public async Task<int> ExecuteAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var failed = false;
        var (connection, tx, owns, scope) = await AcquireAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            bind?.Invoke(command);
            return await command.ExecuteNonQueryAsync(ct);
        }
        catch { failed = true; throw; }
        finally
        {
            RecordDb(sw, failed);
            ReleaseScope(scope);
            if (owns) await connection.DisposeAsync();
        }
    }

    // Executes one write behind a PostgreSQL savepoint when the request is using the
    // ambient RLS transaction.  Expected constraint violations may then be translated
    // into a 409/per-row import error without leaving the whole request transaction in
    // the failed state.  Holding the tenant-scope gate across SAVEPOINT -> statement ->
    // RELEASE prevents another command in the same request from interleaving with it.
    public async Task<int> ExecuteWithSavepointAsync(
        string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var failed = false;
        var (connection, tx, owns, scope) = await AcquireAsync(ct);
        var savepoint = $"opstrax_write_{Interlocked.Increment(ref _savepointSequence)}";
        try
        {
            if (tx is not null)
                await ExecuteControlStatementAsync(connection, tx, $"SAVEPOINT {savepoint}", ct);

            try
            {
                await using var command = new NpgsqlCommand(sql, connection, tx);
                bind?.Invoke(command);
                var affected = await command.ExecuteNonQueryAsync(ct);
                if (tx is not null)
                    await ExecuteControlStatementAsync(connection, tx, $"RELEASE SAVEPOINT {savepoint}", ct);
                return affected;
            }
            catch
            {
                failed = true;
                if (tx is not null)
                    await RollbackSavepointAsync(connection, tx, savepoint);
                throw;
            }
        }
        finally
        {
            RecordDb(sw, failed);
            ReleaseScope(scope);
            if (owns) await connection.DisposeAsync();
        }
    }

    // Runs work inside a serializable transaction, committing on success and rolling
    // back on any exception. Use this when multiple DB writes must be atomic. When an
    // ambient request scope is active, the work joins that transaction (Postgres has
    // no nested transactions) and commit/rollback is deferred to the request scope.
    public async Task<T> WithTransactionAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> work,
        CancellationToken ct = default)
    {
        var scope = _scopes.Current;
        if (scope is not null)
        {
            // Refresh while exclusively holding the connection, then release the
            // gate before invoking user work. Transaction callbacks may call other
            // Database helpers (for example the idempotency service); holding the
            // non-reentrant gate across the callback would self-deadlock. Each nested
            // helper acquires the gate around its own command.
            await scope.Gate.WaitAsync(ct);
            try
            {
                await scope.RefreshTenantTicketIfNeededAsync(ct);
            }
            finally
            {
                ReleaseScope(scope);
            }
            return await work(scope.Connection, scope.Transaction);
        }

        await using var connection = await OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            var result = await work(connection, tx);
            await tx.CommitAsync(ct);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // Appends RETURNING id if not already present, then returns the inserted id.
    public async Task<long> InsertAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        var pgSql = sql.TrimEnd(';', ' ', '\n', '\r');
        if (!pgSql.Contains("RETURNING", StringComparison.OrdinalIgnoreCase))
            pgSql += " RETURNING id";

        var (connection, tx, owns, scope) = await AcquireAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(pgSql, connection, tx);
            bind?.Invoke(command);
            var value = await command.ExecuteScalarAsync(ct);
            return value is null or DBNull ? 0 : Convert.ToInt64(value);
        }
        finally
        {
            ReleaseScope(scope);
            if (owns) await connection.DisposeAsync();
        }
    }

    public async Task<long> InsertWithSavepointAsync(
        string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        var pgSql = sql.TrimEnd(';', ' ', '\n', '\r');
        if (!pgSql.Contains("RETURNING", StringComparison.OrdinalIgnoreCase))
            pgSql += " RETURNING id";

        var (connection, tx, owns, scope) = await AcquireAsync(ct);
        var savepoint = $"opstrax_write_{Interlocked.Increment(ref _savepointSequence)}";
        try
        {
            if (tx is not null)
                await ExecuteControlStatementAsync(connection, tx, $"SAVEPOINT {savepoint}", ct);

            try
            {
                await using var command = new NpgsqlCommand(pgSql, connection, tx);
                bind?.Invoke(command);
                var value = await command.ExecuteScalarAsync(ct);
                if (tx is not null)
                    await ExecuteControlStatementAsync(connection, tx, $"RELEASE SAVEPOINT {savepoint}", ct);
                return value is null or DBNull ? 0 : Convert.ToInt64(value);
            }
            catch
            {
                if (tx is not null)
                    await RollbackSavepointAsync(connection, tx, savepoint);
                throw;
            }
        }
        finally
        {
            ReleaseScope(scope);
            if (owns) await connection.DisposeAsync();
        }
    }

    private static long _savepointSequence;

    private static async Task ExecuteControlStatementAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RollbackSavepointAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string savepoint)
    {
        // Cancellation of the user operation must not prevent transaction recovery.
        // If the connection itself is broken this may also fail, in which case the
        // original statement exception remains the one propagated to the caller.
        try
        {
            await ExecuteControlStatementAsync(
                connection, transaction,
                $"ROLLBACK TO SAVEPOINT {savepoint}; RELEASE SAVEPOINT {savepoint}",
                CancellationToken.None);
        }
        catch { }
    }

    private static string ToCamel(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return value;
        return parts[0].ToLowerInvariant() + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }
}
