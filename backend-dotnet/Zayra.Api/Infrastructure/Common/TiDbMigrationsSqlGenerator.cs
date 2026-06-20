using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Update;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure.Internal;
using Pomelo.EntityFrameworkCore.MySql.Migrations;

namespace Zayra.Api.Infrastructure.Common;

/// <summary>
/// Suppresses <see cref="AlterDatabaseOperation"/> for TiDB Serverless compatibility.
/// TiDB rejects ALTER DATABASE inside the explicit transaction that Pomelo wraps each
/// migration in, causing the whole migration to roll back with no tables created.
/// Skipping the operation is safe: the database charset is already set at creation time.
/// </summary>
public sealed class TiDbMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    ICommandBatchPreparer commandBatchPreparer,
    IMySqlOptions options)
    : MySqlMigrationsSqlGenerator(dependencies, commandBatchPreparer, options)
{
    protected override void Generate(
        AlterDatabaseOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder)
    {
        // No-op: ALTER DATABASE fails inside TiDB's transactional DDL context.
        // The database charset/collation is already correct from the CREATE DATABASE step.
    }
}
