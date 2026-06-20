using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Zayra.Api.Infrastructure.Common;

/// <summary>
/// TiDB Serverless rejects DDL inside an explicit START TRANSACTION / COMMIT block.
/// The default EF Core executor wraps every migration batch in a transaction, which
/// causes all CREATE TABLE / CREATE INDEX statements to fail silently and roll back —
/// leaving only __EFMigrationsHistory (created before the transaction) in place.
///
/// This executor runs each migration command individually without opening any
/// transaction. TiDB auto-commits each DDL statement, which is the correct behaviour.
/// </summary>
public sealed class TiDbMigrationCommandExecutor : IMigrationCommandExecutor
{
    public void ExecuteNonQuery(
        IEnumerable<MigrationCommand> migrationCommands,
        IRelationalConnection connection)
    {
        foreach (var command in migrationCommands)
            command.ExecuteNonQuery(connection);
    }

    public async Task ExecuteNonQueryAsync(
        IEnumerable<MigrationCommand> migrationCommands,
        IRelationalConnection connection,
        CancellationToken cancellationToken = default)
    {
        foreach (var command in migrationCommands)
            await command.ExecuteNonQueryAsync(connection, null, cancellationToken);
    }
}
