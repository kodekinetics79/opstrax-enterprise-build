using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Zayra.Api.Data;

namespace Zayra.Api.Infrastructure.Seed;

/// <summary>
/// EnsureCreated only builds the schema when the database is empty, so entities added
/// after the first deploy never get tables. On every startup this compares the EF model
/// against INFORMATION_SCHEMA and executes the CREATE TABLE / CREATE INDEX statements
/// for whatever is missing — keeping long-lived databases (Railway, local volumes) in
/// sync with the model without migrations.
/// </summary>
public static class MissingTableCreator
{
    public static async Task EnsureAsync(ZayraDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE()";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) existing.Add(reader.GetString(0));
        }

        var script = db.Database.GenerateCreateScript();
        var statements = script.Split(';')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in statements)
        {
            var tableMatch = Regex.Match(statement, @"^CREATE TABLE `([^`]+)`", RegexOptions.IgnoreCase);
            if (tableMatch.Success)
            {
                var table = tableMatch.Groups[1].Value;
                if (existing.Contains(table)) continue;
                await Execute(db, statement, logger, ct);
                created.Add(table);
                continue;
            }

            // Indexes only for tables we just created (existing tables already have theirs).
            var indexMatch = Regex.Match(statement, @"^CREATE (?:UNIQUE )?INDEX `[^`]+` ON `([^`]+)`", RegexOptions.IgnoreCase);
            if (indexMatch.Success && created.Contains(indexMatch.Groups[1].Value))
                await Execute(db, statement, logger, ct);
        }

        if (created.Count > 0)
            logger.LogInformation("MissingTableCreator: created {Count} missing table(s): {Tables}", created.Count, string.Join(", ", created));
    }

    private static async Task Execute(ZayraDbContext db, string sql, ILogger logger, CancellationToken ct)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql, ct); }
        catch (Exception ex) { logger.LogWarning("MissingTableCreator: statement failed ({Message})", ex.Message); }
    }
}
