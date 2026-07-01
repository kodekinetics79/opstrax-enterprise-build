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
    private bool _completed;

    // Serializes command execution on this scope's single shared connection. Under RLS
    // enforcement every query in a request runs on ONE connection/transaction; code that
    // fans out queries concurrently (e.g. Task.WhenAll) would otherwise throw
    // "a command is already in progress". This gate makes such calls queue safely.
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    internal TenantScope(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
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
    // Resolution order: ConnectionStrings:DefaultConnection (incl. the
    // ConnectionStrings__DefaultConnection env var used by docker-compose) →
    // the PG_CONNECTION env var (used by Render / .env). Fails fast if neither is set.
    private readonly string _connectionString =
        Coalesce(
            configuration.GetConnectionString("DefaultConnection"),
            Environment.GetEnvironmentVariable("PG_CONNECTION"))
        ?? throw new InvalidOperationException(
            "Database connection string is not configured. Set ConnectionStrings__DefaultConnection or PG_CONNECTION.");

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

    private static string? Coalesce(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    // Runs off-pipeline work under a platform-admin bypass scope so cross-tenant
    // background processing (which already filters by company_id in SQL) can read/write
    // RLS tables as the restricted role. No-op wrapper when enforcement is off. The
    // ambient scope is assigned from THIS frame so it flows into the awaited body, then
    // always cleared — never leaks onto a pooled connection.
    public async Task RunInSystemScopeAsync(Func<Task> body, CancellationToken ct = default)
    {
        if (!RlsEnforced) { await body(); return; }
        await using var sys = await BeginSystemScopeAsync(ct);
        _scopes.Current = sys;
        try { await body(); await sys.CompleteAsync(ct); }
        finally { _scopes.Current = null; }
    }

    // Runs a single short-lived tenant-scoped read/unit of work and returns its result.
    // Used by the SSE stream, which must NOT hold one request-length transaction open for
    // the connection's whole lifetime: instead each 3s tick opens a scope, reads, commits,
    // and releases the connection. No-op wrapper (fresh per-query connection) when
    // enforcement is off. The ambient scope is set from THIS frame and always cleared.
    public async Task<T> RunInTenantScopeAsync<T>(long companyId, Func<Task<T>> body, CancellationToken ct = default)
    {
        if (!RlsEnforced) return await body();
        await using var scope = await BeginTenantScopeAsync(companyId, ct);
        _scopes.Current = scope;
        try { var result = await body(); await scope.CompleteAsync(ct); return result; }
        finally { _scopes.Current = null; }
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        // Retry transient open failures with backoff. Neon's serverless pooler can
        // cold-start (scale-to-zero) or drop idle connections, so the first attempt
        // after an idle period may transiently fail — retrying avoids surfacing a 500.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var connection = new NpgsqlConnection(_connectionString);
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

    private static bool IsTransient(Exception ex) => ex switch
    {
        NpgsqlException npg => npg.IsTransient,
        TimeoutException => true,
        _ => false,
    };

    // ── Request-scoped tenant context (RLS activation, Option A1) ──────────────────
    // Opens a connection + transaction and sets the tenant GUC as the FIRST statement
    // (SET LOCAL semantics). All queries issued while this scope is the ambient scope
    // run inside the same transaction, so Postgres RLS filters every one of them by
    // `app.current_tenant_id`. The caller commits via CompleteAsync and disposes to
    // release the connection.
    public async Task<TenantScope> BeginTenantScopeAsync(long companyId, CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        var tx = await connection.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("SELECT set_config('app.current_tenant_id', @cid, true)", connection, tx))
        {
            cmd.Parameters.AddWithValue("@cid", companyId.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return new TenantScope(connection, tx);
    }

    // System / platform-admin scope — sets the SEPARATE `app.platform_admin` GUC that
    // the Stage-19 `platform_admin_bypass` policy checks. Used for legitimately
    // cross-tenant work (platform-admin routes, the pre-tenant auth bootstrap,
    // token-scoped public routes, background workers). Never sets a tenant id.
    public async Task<TenantScope> BeginSystemScopeAsync(CancellationToken ct = default)
    {
        var connection = await OpenAsync(ct);
        var tx = await connection.BeginTransactionAsync(ct);
        await using (var cmd = new NpgsqlCommand("SELECT set_config('app.platform_admin', 'on', true)", connection, tx))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        return new TenantScope(connection, tx);
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
            return (scope.Connection, scope.Transaction, false, scope);
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
        finally
        {
            ReleaseScope(scope);
            if (owns) await connection.DisposeAsync();
        }
    }

    public async Task<Dictionary<string, object?>?> QuerySingleAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
        => (await QueryAsync(sql, bind, ct)).FirstOrDefault();

    public async Task<long> ScalarLongAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        var (connection, tx, owns, scope) = await AcquireAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
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
        var (connection, tx, owns, scope) = await AcquireAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            bind?.Invoke(command);
            return await command.ExecuteNonQueryAsync(ct);
        }
        finally
        {
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

    private static string ToCamel(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return value;
        return parts[0].ToLowerInvariant() + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }
}
