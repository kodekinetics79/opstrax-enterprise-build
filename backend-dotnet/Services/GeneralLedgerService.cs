using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// General Ledger posting service. Enforces the two invariants that make it a book of record:
//   (1) every journal entry balances — sum(debit) == sum(credit);
//   (2) each source event posts at most once — UNIQUE(company_id, source_type, source_ref).
// A trial balance over all lines must therefore always net to zero. This is the seam the sub-ledgers
// (AR invoices, AP settlements, tax, rev-rec) post into; AR-invoice posting is wired here first.
public sealed class GeneralLedgerService(Database db)
{
    public readonly record struct Line(string AccountCode, decimal Debit, decimal Credit);

    // Standard minimal chart. Seeded per company on demand (idempotent).
    private static readonly (string Code, string Name, string Type, string Normal)[] DefaultChart =
    {
        ("1000", "Cash",                 "asset",     "debit"),
        ("1100", "Accounts Receivable",  "asset",     "debit"),
        ("2000", "Accounts Payable",     "liability", "credit"),
        ("2200", "Tax Payable",          "liability", "credit"),
        ("4000", "Freight Revenue",      "revenue",   "credit"),
        ("5000", "Driver Pay Expense",   "expense",   "debit"),
    };

    public async Task EnsureChartAsync(long companyId, CancellationToken ct = default)
    {
        foreach (var a in DefaultChart)
            await db.ExecuteAsync(
                @"INSERT INTO chart_of_accounts (company_id, account_code, account_name, account_type, normal_balance)
                  VALUES (@c, @code, @name, @type, @normal)
                  ON CONFLICT (company_id, account_code) DO NOTHING",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@c", companyId);
                    cmd.Parameters.AddWithValue("@code", a.Code);
                    cmd.Parameters.AddWithValue("@name", a.Name);
                    cmd.Parameters.AddWithValue("@type", a.Type);
                    cmd.Parameters.AddWithValue("@normal", a.Normal);
                }, ct);
    }

    // Post a balanced journal entry. Returns the entry id, or the EXISTING id if this source event was
    // already posted (idempotent — never double-posts). Throws if the entry does not balance.
    public async Task<long> PostEntryAsync(
        long companyId, DateTime entryDate, string sourceType, string sourceRef, string? memo,
        IReadOnlyList<Line> lines, CancellationToken ct = default)
    {
        if (lines.Count == 0) throw new InvalidOperationException("A journal entry needs at least one line.");
        var debits = lines.Sum(l => l.Debit);
        var credits = lines.Sum(l => l.Credit);
        if (Math.Round(debits, 2) != Math.Round(credits, 2))
            throw new InvalidOperationException($"Journal entry does not balance: debits {debits:0.00} != credits {credits:0.00}.");

        await EnsureChartAsync(companyId, ct);

        // Idempotency: one entry per (company, source_type, source_ref). If it exists, return it untouched.
        var existing = await db.ScalarLongAsync(
            "SELECT id FROM journal_entries WHERE company_id=@c AND source_type=@t AND source_ref=@r LIMIT 1",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@t", sourceType); c.Parameters.AddWithValue("@r", sourceRef); }, ct);
        if (existing > 0) return existing;

        var entryId = await db.InsertAsync(
            @"INSERT INTO journal_entries (company_id, entry_date, source_type, source_ref, memo)
              VALUES (@c, @d, @t, @r, @memo) RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@c", companyId);
                c.Parameters.AddWithValue("@d", entryDate.Date);
                c.Parameters.AddWithValue("@t", sourceType);
                c.Parameters.AddWithValue("@r", sourceRef);
                c.Parameters.AddWithValue("@memo", (object?)memo ?? DBNull.Value);
            }, ct);

        foreach (var l in lines)
            await db.ExecuteAsync(
                @"INSERT INTO journal_lines (company_id, journal_entry_id, account_code, debit, credit)
                  VALUES (@c, @e, @acct, @dr, @cr)",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId);
                    c.Parameters.AddWithValue("@e", entryId);
                    c.Parameters.AddWithValue("@acct", l.AccountCode);
                    c.Parameters.AddWithValue("@dr", l.Debit);
                    c.Parameters.AddWithValue("@cr", l.Credit);
                }, ct);

        return entryId;
    }

    // Post an issued AR invoice: Dr Accounts Receivable (total), Cr Freight Revenue (subtotal),
    // Cr Tax Payable (tax). Idempotent on the invoice id.
    public async Task<long> PostInvoiceAsync(long companyId, string invoiceId, CancellationToken ct = default)
    {
        var inv = await db.QuerySingleAsync(
            "SELECT subtotal, tax_total, total, issued_at, invoice_number FROM issued_invoices WHERE id=@id::uuid AND company_id=@c",
            c => { c.Parameters.AddWithValue("@id", invoiceId); c.Parameters.AddWithValue("@c", companyId); }, ct);
        if (inv is null) throw new InvalidOperationException($"Issued invoice {invoiceId} not found for company {companyId}.");

        var subtotal = ToDec(inv["subtotal"]);
        var tax = ToDec(inv["taxTotal"]);
        var total = ToDec(inv["total"]);
        var when = inv["issuedAt"] is DateTime dt ? dt : DateTime.UtcNow;

        var lines = new List<Line> { new("1100", total, 0m), new("4000", 0m, subtotal) };
        if (tax > 0) lines.Add(new Line("2200", 0m, tax));

        return await PostEntryAsync(companyId, when, "invoice", invoiceId,
            $"AR invoice {inv["invoiceNumber"]}", lines, ct);
    }

    // Post an APPROVED (or paid) AP settlement statement: accrue the payable.
    // Dr Driver Pay Expense (5000) / Cr Accounts Payable (2000) = statement total.
    // Fail-closed: a draft or zero statement posts nothing (approval is the maker-checker posting control);
    // a missing row throws so the outbox retries/dead-letters. Idempotent on (company, 'settlement', id).
    public async Task<long> PostSettlementAsync(long companyId, long statementId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            "SELECT status, total, statement_no, approved_at, created_at FROM settlement_statements WHERE company_id=@c AND id=@id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", statementId); }, ct);
        if (row is null) throw new InvalidOperationException($"Settlement statement {statementId} not found for company {companyId}.");

        var status = row.GetValueOrDefault("status")?.ToString() ?? "draft";
        if (status is not ("approved" or "paid")) return 0;   // a draft is not a recognized liability
        var total = ToDec(row["total"]);
        if (total <= 0) return 0;

        var when = row["approvedAt"] is DateTime a ? a : row["createdAt"] is DateTime cr ? cr : DateTime.UtcNow;
        return await PostEntryAsync(companyId, when, "settlement", statementId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"AP settlement {row["statementNo"]}",
            new List<Line> { new("5000", total, 0m), new("2000", 0m, total) }, ct);
    }

    // Post an AP settlement PAYMENT: relieve the payable on cash-out.
    // Dr Accounts Payable (2000) / Cr Cash (1000) = payment amount. One entry per payment id, so
    // partial payments accumulate and reconcile. Idempotent on (company, 'settlement_payment', id).
    public async Task<long> PostSettlementPaymentAsync(long companyId, long paymentId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            "SELECT amount, statement_id, paid_at FROM settlement_payments WHERE company_id=@c AND id=@id",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", paymentId); }, ct);
        if (row is null) throw new InvalidOperationException($"Settlement payment {paymentId} not found for company {companyId}.");

        var amount = ToDec(row["amount"]);
        if (amount <= 0) return 0;

        var when = row["paidAt"] is DateTime p ? p : DateTime.UtcNow;
        return await PostEntryAsync(companyId, when, "settlement_payment", paymentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"AP payment {paymentId} on statement {row["statementId"]}",
            new List<Line> { new("2000", amount, 0m), new("1000", 0m, amount) }, ct);
    }

    // Trial balance: per-account debit/credit totals plus the grand totals. In a correct ledger the grand
    // total of debits equals the grand total of credits.
    public async Task<TrialBalance> TrialBalanceAsync(long companyId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT jl.account_code, COALESCE(coa.account_name,'') account_name,
                     SUM(jl.debit) debits, SUM(jl.credit) credits
              FROM journal_lines jl
              LEFT JOIN chart_of_accounts coa ON coa.company_id=jl.company_id AND coa.account_code=jl.account_code
              WHERE jl.company_id=@c
              GROUP BY jl.account_code, coa.account_name
              ORDER BY jl.account_code",
            c => c.Parameters.AddWithValue("@c", companyId), ct);

        var accounts = rows.Select(r => new TrialBalanceRow(
            r["accountCode"]?.ToString() ?? "",
            r["accountName"]?.ToString() ?? "",
            ToDec(r["debits"]), ToDec(r["credits"]))).ToList();

        return new TrialBalance(accounts, accounts.Sum(a => a.Debits), accounts.Sum(a => a.Credits));
    }

    private static decimal ToDec(object? v) => v is null or DBNull ? 0m : Convert.ToDecimal(v);

    public readonly record struct TrialBalanceRow(string AccountCode, string AccountName, decimal Debits, decimal Credits);
    public readonly record struct TrialBalance(IReadOnlyList<TrialBalanceRow> Accounts, decimal TotalDebits, decimal TotalCredits)
    {
        public bool IsBalanced => Math.Round(TotalDebits, 2) == Math.Round(TotalCredits, 2);
    }
}
