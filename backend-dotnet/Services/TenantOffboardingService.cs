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
// Runs as the DB owner (DELETE across all tenants is legitimate admin work). Returns
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
                AND c.table_name <> 'companies'
              ORDER BY c.table_name", conn, tx);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add((reader.GetString(0), reader.GetString(1)));
        return result;
    }

    public async Task<OffboardResult> DeleteTenantAsync(long companyId, CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var tenantTables = await DiscoverTenantTablesAsync(conn, tx, ct);
        var deletedByTable = new Dictionary<string, long>();

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
                    await using var rel = new NpgsqlCommand("RELEASE SAVEPOINT del_sp", conn, tx);
                    await rel.ExecuteNonQueryAsync(ct);
                }
                catch (PostgresException)
                {
                    // FK child still present (delete a later pass) — roll back just this stmt.
                    await using var rb = new NpgsqlCommand("ROLLBACK TO SAVEPOINT del_sp", conn, tx);
                    await rb.ExecuteNonQueryAsync(ct);
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
            throw new InvalidOperationException(
                $"Tenant offboarding aborted for company {companyId}: residual rows remain in {residual.Count} table(s) " +
                $"(likely an FK cycle): {string.Join(", ", residual.Take(10))}. No rows were deleted (transaction rolled back).");
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
