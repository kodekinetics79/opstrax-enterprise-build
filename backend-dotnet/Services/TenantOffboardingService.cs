using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// TENANT OFFBOARDING — provable "delete on request" for the pilot data agreement.
//
// The demo-seeder cleanup bug (a hand-maintained DELETE list that omitted several
// child tables, so the company row could never be removed) is the reason this exists
// as a SCHEMA-DRIVEN mechanism instead of a static list:
//
//   1. Discover EVERY base table carrying a company_id or tenant_id column from
//      information_schema (so a newly-added tenant table can never be silently missed).
//   2. Inside one transaction, delete this company's rows from every such table,
//      iterating in passes: FK-child rows that block a parent delete in one pass are
//      removed in a later pass. Each per-table delete runs in its own SAVEPOINT so an
//      FK violation rolls back only that statement, not the whole run.
//   3. Repeat until a full pass removes zero rows (stable) — then delete the companies
//      row itself. If any tenant rows remain (a cycle we couldn't break), the whole
//      transaction rolls back and we throw, rather than half-deleting a tenant.
//
// Runs as the narrowly-granted opstrax_system identity (DELETE across all tenants is
// legitimate audited admin work). Returns
// per-table counts so the platform endpoint + tests can assert zero rows remain.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TenantOffboardingService(Database db)
{
    public sealed record OffboardResult(
        long CompanyId,
        bool CompanyDeleted,
        long TotalRowsDeleted,
        IReadOnlyDictionary<string, long> DeletedByTable,
        IReadOnlyList<string> TablesWithResidualRows);

    // Discover (table, tenant-column) pairs. A table may appear twice if it carries
    // both company_id and tenant_id (e.g. ai_recommendations) — we delete on either.
    private async Task<List<(string Table, string Column)>> DiscoverTenantTablesAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct)
    {
        var result = new List<(string, string)>();
        await using var cmd = new NpgsqlCommand(
            @"SELECT c.table_name, c.column_name
              FROM information_schema.columns c
              JOIN information_schema.tables t
                ON t.table_name = c.table_name AND t.table_schema = c.table_schema
              WHERE c.table_schema = 'public'
                AND t.table_type = 'BASE TABLE'
                AND c.column_name IN ('company_id','tenant_id')
                AND c.data_type = 'bigint'
                AND c.table_name <> 'companies'
              ORDER BY c.table_name", conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add((reader.GetString(0), reader.GetString(1)));
        return result;
    }

    // Nullable references between tenant-owned tables may form legitimate operational
    // cycles (for example drivers.assigned_vehicle_id <-> vehicles.assigned_driver_id).
    // Null them inside the same transaction before deletion. The discovery is schema-
    // driven, limited to single-column nullable FKs whose child and parent are both
    // tenant-owned, and scoped to the target tenant. If anything later fails, the
    // transaction rolls these updates back together with every delete.
    private async Task BreakNullableTenantForeignKeysAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, long companyId, CancellationToken ct)
    {
        var links = new List<(string Table, string Column, bool HasCompanyId, bool HasTenantId)>();
        await using (var cmd = new NpgsqlCommand(
            @"WITH tenant_tables AS (
                  SELECT table_name,
                         bool_or(column_name='company_id' AND data_type='bigint') has_company_id,
                         bool_or(column_name='tenant_id' AND data_type='bigint') has_tenant_id
                  FROM information_schema.columns
                  WHERE table_schema='public'
                  GROUP BY table_name
                  HAVING bool_or(column_name IN ('company_id','tenant_id') AND data_type='bigint')
              )
              SELECT child.relname, attr.attname, owned.has_company_id, owned.has_tenant_id
              FROM pg_constraint fk
              JOIN pg_class child ON child.oid=fk.conrelid
              JOIN pg_namespace child_ns ON child_ns.oid=child.relnamespace AND child_ns.nspname='public'
              JOIN pg_class parent ON parent.oid=fk.confrelid
              JOIN pg_namespace parent_ns ON parent_ns.oid=parent.relnamespace AND parent_ns.nspname='public'
              JOIN tenant_tables owned ON owned.table_name=child.relname
              JOIN tenant_tables parent_owned ON parent_owned.table_name=parent.relname
              JOIN pg_attribute attr ON attr.attrelid=child.oid AND attr.attnum=fk.conkey[1]
              WHERE fk.contype='f' AND cardinality(fk.conkey)=1 AND NOT attr.attnotnull
              ORDER BY child.relname, attr.attname", conn, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                links.Add((reader.GetString(0), reader.GetString(1), reader.GetBoolean(2), reader.GetBoolean(3)));
        }

        foreach (var (table, column, hasCompanyId, hasTenantId) in links)
        {
            var scope = hasCompanyId && hasTenantId
                ? "(company_id=@cid OR tenant_id=@cid)"
                : hasCompanyId ? "company_id=@cid" : "tenant_id=@cid";
            await using var update = new NpgsqlCommand(
                $"UPDATE \"{table}\" SET \"{column}\"=NULL WHERE {scope} AND \"{column}\" IS NOT NULL",
                conn, tx);
            update.Parameters.AddWithValue("@cid", companyId);
            await update.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<OffboardResult> DeleteTenantAsync(long companyId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenSystemAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Immutable compliance triggers remain fail-closed for ordinary application
        // work. A legal tenant offboarding is the one narrow exception: it runs only
        // through the system identity, inside this transaction, and the Stage 72
        // trigger contract checks both this transaction-local flag and membership in
        // opstrax_system. The flag cannot leak through the connection pool.
        await using (var authorize = new NpgsqlCommand(
            @"DO $$ BEGIN
                IF NOT pg_has_role(current_user,'opstrax_system','MEMBER') THEN
                  RAISE EXCEPTION 'Tenant offboarding requires opstrax_system authority';
                END IF;
              END $$;
              SELECT set_config('opstrax.offboarding','on',true);", conn, tx))
            await authorize.ExecuteNonQueryAsync(ct);

        var tenantTables = await DiscoverTenantTablesAsync(conn, tx, ct);
        var deletedByTable = new Dictionary<string, long>();
        var lastBlockedDelete = new Dictionary<string, string>();

        await BreakNullableTenantForeignKeysAsync(conn, tx, companyId, ct);

        // Iterative passes: keep deleting until a whole pass removes nothing. Bounded by
        // table count so a genuine FK cycle can't loop forever.
        var maxPasses = tenantTables.Count + 2;
        for (var pass = 0; pass < maxPasses; pass++)
        {
            long removedThisPass = 0;
            foreach (var (table, column) in tenantTables)
            {
                await using (var sp = new NpgsqlCommand("SAVEPOINT del_sp", conn, tx))
                    await sp.ExecuteNonQueryAsync(ct);
                try
                {
                    await using var del = new NpgsqlCommand(
                        $"DELETE FROM \"{table}\" WHERE {column} = @cid", conn, tx);
                    del.Parameters.AddWithValue("@cid", companyId);
                    var n = await del.ExecuteNonQueryAsync(ct);
                    if (n > 0)
                    {
                        removedThisPass += n;
                        deletedByTable[table] = deletedByTable.GetValueOrDefault(table) + n;
                    }
                    lastBlockedDelete.Remove(table);
                    await using var rel = new NpgsqlCommand("RELEASE SAVEPOINT del_sp", conn, tx);
                    await rel.ExecuteNonQueryAsync(ct);
                }
                catch (PostgresException ex)
                {
                    // FK child still present (delete a later pass) — roll back just this stmt.
                    await using var rb = new NpgsqlCommand("ROLLBACK TO SAVEPOINT del_sp", conn, tx);
                    await rb.ExecuteNonQueryAsync(ct);
                    lastBlockedDelete[table] = $"{ex.SqlState}:{ex.ConstraintName ?? ex.MessageText}";
                }
            }
            if (removedThisPass == 0) break;
        }

        // Verify no tenant rows survive before we touch companies.
        var residual = new List<string>();
        foreach (var (table, column) in tenantTables)
        {
            await using var cnt = new NpgsqlCommand(
                $"SELECT EXISTS(SELECT 1 FROM \"{table}\" WHERE {column} = @cid)", conn, tx);
            cnt.Parameters.AddWithValue("@cid", companyId);
            if (await cnt.ExecuteScalarAsync(ct) is true)
                residual.Add($"{table}.{column}");
        }

        if (residual.Count > 0)
        {
            await tx.RollbackAsync(ct);
            var blockers = string.Join("; ", residual.Take(10)
                .Select(item => item.Split('.')[0]).Distinct()
                .Where(lastBlockedDelete.ContainsKey)
                .Select(table => $"{table}={lastBlockedDelete[table]}"));
            throw new InvalidOperationException(
                $"Tenant offboarding aborted for company {companyId}: residual rows remain in {residual.Count} table(s) " +
                $"(likely an FK cycle): {string.Join(", ", residual.Take(10))}. " +
                $"Last blocked deletes: {blockers}. No rows were deleted (transaction rolled back).");
        }

        // Finally remove the company itself.
        long companyDeleted;
        await using (var delCompany = new NpgsqlCommand("DELETE FROM companies WHERE id = @cid", conn, tx))
        {
            delCompany.Parameters.AddWithValue("@cid", companyId);
            companyDeleted = await delCompany.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        return new OffboardResult(
            companyId,
            companyDeleted > 0,
            deletedByTable.Values.Sum(),
            deletedByTable,
            residual);
    }
}
