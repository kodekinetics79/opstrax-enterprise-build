using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed class CoreSchemaService(Database db, ILogger<CoreSchemaService> log)
{
    public async Task EnsureAsync(CancellationToken ct = default)
    {
        var path = ResolveSchemaPath();
        var sql = await File.ReadAllTextAsync(path, ct);

        foreach (var statement in SplitStatements(sql))
        {
            try
            {
                await db.ExecuteAsync(statement, ct: ct);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateObject)
            {
                log.LogDebug("Core schema object already exists while applying {SchemaPath}: {MessageText}", path, ex.MessageText);
            }
        }

        await EnsureCoreSeedAsync(ct);
    }

    private async Task EnsureCoreSeedAsync(CancellationToken ct)
    {
        await db.ExecuteAsync(
            @"INSERT INTO roles (name, permissions_json, is_system)
              SELECT name, permissions_json, TRUE
              FROM (VALUES
                ('Super Admin',              jsonb_build_array('*')),
                ('Company Admin',            jsonb_build_array('*')),
                ('Fleet Manager',            jsonb_build_array('dashboard:view','fleet:view','fleet:manage','maintenance:view','maintenance:manage','telematics:view','dispatch:view','intelligence:view','map:view')),
                ('Dispatcher',               jsonb_build_array('dashboard:view','dispatch:view','dispatch:manage','fleet:view','jobs:view','jobs:manage','map:view','customers:view')),
                ('Driver',                   jsonb_build_array('driver:portal','jobs:view','dvir:manage')),
                ('Mechanic',                 jsonb_build_array('maintenance:view','maintenance:manage','dvir:review','fleet:view')),
                ('Safety Manager',           jsonb_build_array('dashboard:view','safety:view','safety:manage','compliance:view','fleet:view','telematics:view','intelligence:view')),
                ('Compliance Manager',       jsonb_build_array('dashboard:view','compliance:view','compliance:manage','audit:view','fleet:view','intelligence:view')),
                ('Customer Service',         jsonb_build_array('customers:view','customer-portal:view','dispatch:view','crm:view')),
                ('Customer Portal User',     jsonb_build_array('customer-portal:view')),
                ('Reseller / Partner Admin', jsonb_build_array('*')),
                ('Read-only Auditor',        jsonb_build_array('audit:view','fleet:view','dashboard:view')),
                ('Operations Manager',       jsonb_build_array('dashboard:view','map:view','fleet:view','dispatch:view','dispatch:manage','orders:view','orders:manage','shipments:view','shipments:manage','pod:view','pod:upload','maintenance:view','safety:view','dashcam:view','compliance:view','reports:view','settings:view')),
                ('Finance & Billing Manager',jsonb_build_array('finance:view','finance:manage','fuel:view','fuel:manage','reports:view','settings:view')),
                ('CRM & Sales Manager',      jsonb_build_array('crm:view','crm:manage','campaigns:view','campaigns:manage','customer_portal:view','reports:view')),
                ('Vendor Service Provider',  jsonb_build_array('vendor_portal:view','maintenance:view','pod:view'))
              ) seed(name, permissions_json)
              WHERE NOT EXISTS (
                SELECT 1 FROM roles r
                WHERE r.company_id IS NULL AND LOWER(r.name) = LOWER(seed.name)
              )",
            ct: ct);
    }

    private static string ResolveSchemaPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Schema", "001_schema.sql"),
            Path.Combine(Directory.GetCurrentDirectory(), "database", "init", "001_schema.sql"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "init", "001_schema.sql"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Core schema file database/init/001_schema.sql was not found.");
    }

    private static IEnumerable<string> SplitStatements(string sql)
    {
        var start = 0;
        var inSingleQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '\'')
            {
                if (inSingleQuote && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
            }
            else if (sql[i] == ';' && !inSingleQuote)
            {
                var statement = sql[start..i].Trim();
                if (statement.Length > 0) yield return statement;
                start = i + 1;
            }
        }

        var tail = sql[start..].Trim();
        if (tail.Length > 0) yield return tail;
    }
}
