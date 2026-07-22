using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public sealed record GlExportResult(
    bool Ok, string? Reason, string? FileName, byte[]? Bytes, string? Checksum,
    int RowCount, decimal TotalDebits, decimal TotalCredits);

// ERP journal export (blueprint period-close-erp-export). Deterministic (ORDER BY entry_date, entry id,
// line id + invariant money formatting => byte-identical re-export), fail-closed (refuses an unbalanced
// or empty period so a malformed journal never reaches QuickBooks/NetSuite), audited (one append-only
// gl_export_runs row per call), and anti-CSV-injection hardened.
public sealed class GeneralLedgerExportService(Database db)
{
    public static readonly string[] Formats = ["csv", "quickbooks", "netsuite"];

    public async Task<GlExportResult> BuildExportAsync(long companyId, string periodCode, string format, long userId, CancellationToken ct = default)
    {
        format = format.ToLowerInvariant();
        if (!Formats.Contains(format)) return Fail("unsupported_format");

        var period = await db.QuerySingleAsync(
            "SELECT period_start, period_end FROM gl_periods WHERE company_id=@c AND period_code=@code",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@code", periodCode); }, ct);
        if (period is null) return Fail("period_not_found");
        var start = Convert.ToDateTime(period["periodStart"]);
        var end = Convert.ToDateTime(period["periodEnd"]);

        var rows = await db.QueryAsync(
            @"SELECT je.id entry_id, je.entry_date, je.source_type, je.source_ref, je.memo,
                     jl.id line_id, jl.account_code, COALESCE(coa.account_name,'') account_name,
                     COALESCE(coa.account_type,'') account_type, jl.debit, jl.credit
              FROM journal_lines jl
              JOIN journal_entries je ON je.id=jl.journal_entry_id
              LEFT JOIN chart_of_accounts coa ON coa.company_id=jl.company_id AND coa.account_code=jl.account_code
              WHERE jl.company_id=@c AND je.entry_date BETWEEN @s AND @e
              ORDER BY je.entry_date, je.id, jl.id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@s", start.Date); c.Parameters.AddWithValue("@e", end.Date); }, ct);

        if (rows.Count == 0) return Fail("empty_period");

        var totalDebits = rows.Sum(r => ToDec(r["debit"]));
        var totalCredits = rows.Sum(r => ToDec(r["credit"]));
        if (Math.Round(totalDebits, 2) != Math.Round(totalCredits, 2))
            return Fail("unbalanced", rows.Count, totalDebits, totalCredits);

        var sb = new StringBuilder();
        switch (format)
        {
            case "csv":
                sb.AppendLine("company_id,period_code,entry_id,entry_date,source_type,source_ref,memo,account_code,account_name,account_type,debit,credit");
                foreach (var r in rows)
                    sb.AppendLine(Row(companyId.ToString(CultureInfo.InvariantCulture), periodCode,
                        S(r["entryId"]), Date(r["entryDate"], "yyyy-MM-dd"), S(r["sourceType"]), S(r["sourceRef"]), S(r["memo"]),
                        S(r["accountCode"]), S(r["accountName"]), S(r["accountType"]), Money(r["debit"]), Money(r["credit"])));
                break;

            case "quickbooks":
                sb.AppendLine("JournalNo,JournalDate,Currency,Memo,AccountNumber,Account,Debits,Credits,Description,Name");
                foreach (var r in rows)
                    sb.AppendLine(Row(S(r["entryId"]), Date(r["entryDate"], "MM/dd/yyyy"), "USD", S(r["memo"]),
                        S(r["accountCode"]), S(r["accountName"]), MoneyOrBlank(r["debit"]), MoneyOrBlank(r["credit"]), S(r["memo"]), ""));
                break;

            case "netsuite":
                sb.AppendLine("ExternalId,TranDate,Subsidiary,Currency,Memo,Account,Debit,Credit,Line Memo");
                foreach (var r in rows)
                    sb.AppendLine(Row($"OPSTRAX-JE-{companyId}-{S(r["entryId"])}", Date(r["entryDate"], "M/d/yyyy"), "", "USD",
                        S(r["memo"]), S(r["accountCode"]), MoneyOrBlank(r["debit"]), MoneyOrBlank(r["credit"]), S(r["memo"])));
                break;
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        // Checksum over the same ordered tuple the close uses, so a closed period's export can be
        // cross-checked against gl_periods.close_checksum.
        var tuple = string.Join("|", rows.Select(r =>
            $"{S(r["entryId"])}:{S(r["accountCode"])}:{ToDec(r["debit"]).ToString(CultureInfo.InvariantCulture)}:{ToDec(r["credit"]).ToString(CultureInfo.InvariantCulture)}:{Date(r["entryDate"], "yyyy-MM-dd")}"));
        var checksum = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(tuple))).ToLowerInvariant();
        var fileName = $"gl-{format}-{companyId}-{periodCode}.csv";

        await db.ExecuteAsync(
            @"INSERT INTO gl_export_runs (company_id, period_code, format, row_count, total_debits, total_credits, checksum, file_name, exported_by_user_id)
              VALUES (@c, @code, @f, @n, @d, @cr, @sum, @file, @u)",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@code", periodCode);
                c.Parameters.AddWithValue("@f", format);
                c.Parameters.AddWithValue("@n", rows.Count);
                c.Parameters.AddWithValue("@d", totalDebits);
                c.Parameters.AddWithValue("@cr", totalCredits);
                c.Parameters.AddWithValue("@sum", checksum);
                c.Parameters.AddWithValue("@file", fileName);
                c.Parameters.AddWithValue("@u", userId);
            }, ct);

        return new GlExportResult(true, null, fileName, bytes, checksum, rows.Count, totalDebits, totalCredits);
    }

    private static GlExportResult Fail(string reason, int rows = 0, decimal d = 0, decimal c = 0) =>
        new(false, reason, null, null, null, rows, d, c);

    private static decimal ToDec(object? v) => v is null or DBNull ? 0m : Convert.ToDecimal(v);
    private static string S(object? v) => v is null or DBNull ? "" : v.ToString() ?? "";
    private static string Date(object? v, string fmt) => v is DateTime dt ? dt.ToString(fmt, CultureInfo.InvariantCulture) : "";
    private static string Money(object? v) => ToDec(v).ToString("F2", CultureInfo.InvariantCulture);
    private static string MoneyOrBlank(object? v) { var d = ToDec(v); return d == 0m ? "" : d.ToString("F2", CultureInfo.InvariantCulture); }

    private static string Row(params string[] cells) => string.Join(",", cells.Select(Escape));

    // RFC-escape + anti-CSV-injection: a cell starting with = + - or @ gets a leading apostrophe so a
    // spreadsheet never executes it as a formula.
    private static string Escape(string cell)
    {
        if (cell.Length > 0 && cell[0] is '=' or '+' or '-' or '@') cell = "'" + cell;
        if (cell.Contains(',') || cell.Contains('"') || cell.Contains('\n') || cell.Contains('\r'))
            cell = "\"" + cell.Replace("\"", "\"\"") + "\"";
        return cell;
    }
}
