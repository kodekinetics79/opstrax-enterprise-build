using System.Text.RegularExpressions;
using Npgsql;

namespace Opstrax.Tests;

public sealed class TenantIdentityDefaultMigrationTests
{
    private const string MigrationName = "2026_09_15_stage144_remove_tenant_identity_defaults";

    [Fact]
    public void Migration_IsCatalogDrivenFailClosedAndEnrolled()
    {
        var sql = MigrationSql();
        var runner = Read("tools", "apply-neon-predeploy-migrations.sh");

        Assert.Contains("information_schema.columns", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("c.column_name IN ('company_id', 'tenant_id')", sql, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE %I.%I ALTER COLUMN %I DROP DEFAULT", sql, StringComparison.Ordinal);
        Assert.Contains("RAISE EXCEPTION 'unsafe tenant identity defaults remain", sql, StringComparison.Ordinal);
        Assert.Contains($"'{MigrationName}'", sql, StringComparison.Ordinal);
        Assert.Contains(MigrationName, runner, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericModuleInsert_BindsAuthenticatedCompanyExplicitly()
    {
        var endpoints = Read("backend-dotnet", "Controllers", "EndpointMappings.cs");
        Assert.Contains(
            "INSERT INTO module_records (company_id, module_key, title, status",
            endpoints,
            StringComparison.Ordinal);
        Assert.Contains("VALUES (@companyId, @key, @title", endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShippedCatalogPass_RemovesLiteralFallbacksAndItsAssertionPasses()
    {
        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var table = $"stage144_default_probe_{Guid.NewGuid():N}";

        try
        {
            await Execute(connection, transaction,
                $"CREATE TABLE public.{table} (id BIGINT GENERATED ALWAYS AS IDENTITY, company_id BIGINT DEFAULT 1, tenant_id INTEGER DEFAULT (1))");

            var sql = MigrationSql();
            await Execute(connection, transaction, ExtractDoBlock(sql, "remove_tenant_defaults"));
            await Execute(connection, transaction, ExtractDoBlock(sql, "assert_no_tenant_defaults"));

            var defaults = await Scalar<long>(connection, transaction,
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND table_name=@table AND column_name IN ('company_id','tenant_id') AND column_default IS NOT NULL",
                command => command.Parameters.AddWithValue("table", table));
            Assert.Equal(0, defaults);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static string ExtractDoBlock(string sql, string tag)
    {
        var match = Regex.Match(sql, $@"DO \${tag}\$.*?\${tag}\$;", RegexOptions.Singleline);
        Assert.True(match.Success, $"{tag} block missing from shipped migration");
        return match.Value;
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, Action<NpgsqlCommand> bind)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        bind(command);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private static string MigrationSql() => Read("database", "migrations", MigrationName + ".sql");

    private static string Read(params string[] segments)
    {
        var parts = new[] { RepoRoot() }.Concat(segments).ToArray();
        return File.ReadAllText(Path.Combine(parts));
    }

    private static string RepoRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
}
