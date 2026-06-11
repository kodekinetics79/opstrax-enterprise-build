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
        var existingColumns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = DATABASE()";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var table = reader.GetString(0);
                existing.Add(table);
                if (!existingColumns.TryGetValue(table, out var cols))
                    existingColumns[table] = cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                cols.Add(reader.GetString(1));
            }
        }

        var script = db.Database.GenerateCreateScript();
        var statements = script.Split(';')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedColumns = 0;
        foreach (var statement in statements)
        {
            var tableMatch = Regex.Match(statement, @"^CREATE TABLE `([^`]+)`", RegexOptions.IgnoreCase);
            if (tableMatch.Success)
            {
                var table = tableMatch.Groups[1].Value;
                if (!existing.Contains(table))
                {
                    await Execute(db, statement, logger, ct);
                    created.Add(table);
                    continue;
                }

                // Table exists: add any columns the model has gained since the table was created.
                foreach (var columnDef in ParseColumnDefinitions(statement))
                {
                    var columnName = Regex.Match(columnDef, "^`([^`]+)`").Groups[1].Value;
                    if (existingColumns[table].Contains(columnName)) continue;
                    await Execute(db, $"ALTER TABLE `{table}` ADD COLUMN {columnDef}", logger, ct);
                    logger.LogInformation("MissingTableCreator: added column {Table}.{Column}", table, columnName);
                    addedColumns++;
                }
                continue;
            }

            // Indexes only for tables we just created (existing tables already have theirs).
            var indexMatch = Regex.Match(statement, @"^CREATE (?:UNIQUE )?INDEX `[^`]+` ON `([^`]+)`", RegexOptions.IgnoreCase);
            if (indexMatch.Success && created.Contains(indexMatch.Groups[1].Value))
                await Execute(db, statement, logger, ct);
        }

        if (created.Count > 0 || addedColumns > 0)
            logger.LogInformation("MissingTableCreator: created {Tables} missing table(s), added {Columns} missing column(s){List}",
                created.Count, addedColumns, created.Count > 0 ? ": " + string.Join(", ", created) : string.Empty);
    }

    /// <summary>Extracts plain column definition lines (no PK/constraint/index clauses) from a CREATE TABLE statement.</summary>
    private static IEnumerable<string> ParseColumnDefinitions(string createTable)
    {
        var open = createTable.IndexOf('(');
        if (open < 0) yield break;
        var body = createTable[(open + 1)..createTable.LastIndexOf(')')];
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd(',').Trim();
            if (!line.StartsWith('`')) continue; // skips CONSTRAINT / PRIMARY KEY / KEY clauses
            if (line.Contains("AUTO_INCREMENT", StringComparison.OrdinalIgnoreCase)) continue; // key column, always exists
            yield return line;
        }
    }

    private static async Task Execute(ZayraDbContext db, string sql, ILogger logger, CancellationToken ct)
    {
        try { await db.Database.ExecuteSqlRawAsync(sql, ct); }
        catch (Exception ex) { logger.LogWarning("MissingTableCreator: statement failed ({Message})", ex.Message); }
    }
}
