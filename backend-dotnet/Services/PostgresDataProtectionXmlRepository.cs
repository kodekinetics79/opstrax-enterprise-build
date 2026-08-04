using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Npgsql;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

/// <summary>
/// Durable ASP.NET Core Data Protection key repository shared by every API instance.
/// XML is certificate-encrypted before it reaches this class. The repository always
/// opens the dedicated opstrax_system pool and never exposes payloads in diagnostics.
/// </summary>
public sealed class PostgresDataProtectionXmlRepository(Database database) : IXmlRepository
{
    private const int MaxXmlBytes = 1_048_576;

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var connection = database.OpenSystemAsync().GetAwaiter().GetResult();
        using var command = new NpgsqlCommand(
            "SELECT xml_payload FROM platform_data_protection_keys ORDER BY id", connection);
        using var reader = command.ExecuteReader();
        var result = new List<XElement>();
        while (reader.Read())
            result.Add(XElement.Parse(reader.GetString(0), LoadOptions.PreserveWhitespace));
        return result;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (string.IsNullOrWhiteSpace(friendlyName) || friendlyName.Length > 256)
            throw new ArgumentException("Data Protection key name is invalid.", nameof(friendlyName));

        var xml = element.ToString(SaveOptions.DisableFormatting);
        if (System.Text.Encoding.UTF8.GetByteCount(xml) > MaxXmlBytes)
            throw new InvalidOperationException("Data Protection key payload exceeds the repository limit.");

        using var connection = database.OpenSystemAsync().GetAwaiter().GetResult();
        using var command = new NpgsqlCommand(
            @"INSERT INTO platform_data_protection_keys(friendly_name,xml_payload)
              VALUES(@name,@xml)
              ON CONFLICT(friendly_name) DO NOTHING", connection);
        command.Parameters.AddWithValue("@name", friendlyName);
        command.Parameters.AddWithValue("@xml", xml);
        command.ExecuteNonQuery();
    }

    public async Task<long> ProbeAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenSystemAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM platform_data_protection_keys", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct));
    }

    public async Task<bool> HasExpectedSchemaContractAsync(CancellationToken ct = default)
    {
        await using var connection = await database.OpenSystemAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            WITH target AS (
              SELECT 'public.platform_data_protection_keys'::regclass AS oid
            ), attrs AS (
              SELECT a.* FROM pg_attribute a,target t
               WHERE a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped
            )
            SELECT
              (SELECT count(*) FROM attrs)=4
              AND EXISTS (SELECT 1 FROM attrs WHERE attname='id'
                    AND atttypid='bigint'::regtype AND attnotnull AND attidentity='a')
              AND EXISTS (SELECT 1 FROM attrs WHERE attname='friendly_name'
                    AND atttypid='character varying'::regtype AND atttypmod=260
                    AND attnotnull AND attidentity='')
              AND EXISTS (SELECT 1 FROM attrs WHERE attname='xml_payload'
                    AND atttypid='text'::regtype AND atttypmod=-1
                    AND attnotnull AND attidentity='')
              AND EXISTS (SELECT 1 FROM attrs WHERE attname='created_at'
                    AND atttypid='timestamp with time zone'::regtype
                    AND attnotnull AND attidentity='')
              AND (SELECT count(*) FROM pg_constraint c,target t
                    WHERE c.conrelid=t.oid AND c.contype='p')=1
              AND EXISTS (SELECT 1 FROM pg_constraint c,target t
                    WHERE c.conrelid=t.oid AND c.contype='p'
                      AND c.conkey=ARRAY[(SELECT attnum FROM attrs WHERE attname='id')]::smallint[])
              AND (SELECT count(*) FROM pg_constraint c,target t
                    WHERE c.conrelid=t.oid AND c.contype='u')=1
              AND EXISTS (SELECT 1 FROM pg_constraint c,target t
                    WHERE c.conrelid=t.oid AND c.contype='u'
                      AND c.conkey=ARRAY[(SELECT attnum FROM attrs WHERE attname='friendly_name')]::smallint[])
              AND (SELECT count(*) FROM pg_constraint c,target t
                    WHERE c.conrelid=t.oid AND c.contype='c')=1
              AND EXISTS (SELECT 1 FROM pg_constraint c,target t
                    WHERE c.conrelid=t.oid AND c.contype='c'
                      AND regexp_replace(pg_get_expr(c.conbin,c.conrelid),'[()[:space:]]','','g')
                          ='octet_lengthxml_payload<=1048576')
              AND pg_get_serial_sequence('public.platform_data_protection_keys','id')
                    ='public.platform_data_protection_keys_id_seq'
              AND EXISTS (SELECT 1 FROM pg_attrdef d
                    JOIN pg_attribute a ON a.attrelid=d.adrelid AND a.attnum=d.adnum,target t
                    WHERE d.adrelid=t.oid AND a.attname='created_at'
                      AND regexp_replace(pg_get_expr(d.adbin,d.adrelid),'[()[:space:]]','','g')='clock_timestamp')
            """, connection);
        return await command.ExecuteScalarAsync(ct) is true;
    }
}
