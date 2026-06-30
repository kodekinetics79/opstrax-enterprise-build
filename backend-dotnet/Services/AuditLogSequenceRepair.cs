using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

internal static class AuditLogSequenceRepair
{
    public static async Task ExecuteWithSequenceRepairAsync(
        Database db,
        string tableName,
        string identityColumn,
        string sql,
        Action<NpgsqlCommand> bind,
        CancellationToken ct = default)
    {
        try
        {
            await db.ExecuteAsync(sql, bind, ct);
            return;
        }
        catch (PostgresException ex) when (IsSequenceConflict(ex, tableName))
        {
            await RepairSequenceAsync(db, tableName, identityColumn, ct);
            await db.ExecuteAsync(sql, bind, ct);
        }
    }

    private static bool IsSequenceConflict(PostgresException ex, string tableName)
        => string.Equals(ex.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal)
           && string.Equals(ex.ConstraintName, $"{tableName}_pkey", StringComparison.OrdinalIgnoreCase);

    private static async Task RepairSequenceAsync(Database db, string tableName, string identityColumn, CancellationToken ct)
    {
        var sql = $"""
                  SELECT setval(
                      pg_get_serial_sequence('{tableName}', '{identityColumn}'),
                      GREATEST((SELECT COALESCE(MAX({identityColumn}), 0) FROM {tableName}), 1),
                      true
                  )
                  """;

        await db.ExecuteAsync(sql, ct: ct);
    }
}
