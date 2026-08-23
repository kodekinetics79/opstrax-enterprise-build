using System.Globalization;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// Customer-facing portal service. Every method is scoped by BOTH company_id AND
// customer_id — a customer-within-tenant isolation boundary that is STRICTER than
// tenant RBAC (two customers in the same company must not see each other's data).
// Internal fields (cost/margin/revenue, driver risk, dispatcher notes, internal AI)
// are stripped at the QUERY level: each SELECT lists only customer-appropriate
// columns, so withheld data never leaves the database.
public sealed class CustomerPortalService(Database db)
{
    // Resolve the authenticated user's bound customer_id. Returns null when the user
    // is not a portal customer-user (internal staff) — the endpoint then denies access.
    //
    // DEF-027 fail-closed: a binding that points at a deleted or nonexistent customer
    // resolves to NULL as well, so the caller lands on the explicit 403 ("not a
    // customer-portal user") instead of a silent empty portal that looks like an
    // operations outage. The join validates the customer against THIS tenant only.
    public async Task<long?> ResolveCustomerIdForUserAsync(long companyId, long userId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT u.customer_id, c.id validated_customer_id
              FROM users u
              LEFT JOIN customers c ON c.id = u.customer_id AND c.company_id = u.company_id AND c.deleted_at IS NULL
              WHERE u.id=@userId AND u.company_id=@companyId AND u.status='Active' LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@userId", userId);
                c.Parameters.AddWithValue("@companyId", companyId);
            }, ct);
        if (row is null || !row.TryGetValue("customerId", out var value) || value is null or DBNull) return null;
        // Bound, but the customer is gone (deleted or never existed): fail closed.
        if (!row.TryGetValue("validatedCustomerId", out var validated) || validated is null or DBNull) return null;
        return Convert.ToInt64(validated, CultureInfo.InvariantCulture);
    }

    // Own invoices (customer-safe fields only) + their payments + a plain-English AR
    // status ("Paid" / "Due in N days" / "Overdue N days"). No margin/cost/internal refs.
    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetOwnInvoicesAsync(long companyId, long customerId, CancellationToken ct = default)
    {
        var invoices = await db.QueryAsync(
            @"SELECT id, invoice_number, currency, subtotal, tax_total, total, amount_paid, balance_due, payment_status, issued_at, due_at, paid_at
              FROM issued_invoices
              WHERE company_id=@companyId AND customer_id=@customerId
              ORDER BY issued_at DESC, invoice_number",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            }, ct);

        var payments = await db.QueryAsync(
            @"SELECT p.issued_invoice_id, p.payment_reference, p.amount, p.currency, p.payment_method, p.received_at
              FROM invoice_payments p
              JOIN issued_invoices i ON i.id = p.issued_invoice_id AND i.company_id = p.company_id
              WHERE p.company_id=@companyId AND i.customer_id=@customerId
              ORDER BY p.received_at",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            }, ct);

        var paymentsByInvoice = payments
            .GroupBy(p => p["issuedInvoiceId"]?.ToString())
            .ToDictionary(g => g.Key ?? "", g => g.Select(p => new Dictionary<string, object?>
            {
                ["paymentReference"] = p["paymentReference"],
                ["amount"] = p["amount"],
                ["currency"] = p["currency"],
                ["paymentMethod"] = p["paymentMethod"],
                ["receivedAt"] = p["receivedAt"],
            }).ToList());

        var result = new List<Dictionary<string, object?>>();
        foreach (var inv in invoices)
        {
            var id = inv["id"]?.ToString() ?? "";
            var balance = Dec(inv, "balanceDue");
            var dueAt = DtoN(inv, "dueAt");
            var view = new Dictionary<string, object?>
            {
                // The id was queried and then dropped, which is why the portal's
                // invoice cards could never open. It is an opaque UUID scoped by
                // company_id AND customer_id on every read, so exposing it does
                // not widen the boundary.
                ["id"] = inv["id"],
                ["invoiceNumber"] = inv["invoiceNumber"],
                ["currency"] = inv["currency"],
                ["subtotal"] = inv["subtotal"],
                ["taxTotal"] = inv["taxTotal"],
                ["total"] = inv["total"],
                ["amountPaid"] = inv["amountPaid"],
                ["balanceDue"] = inv["balanceDue"],
                ["issuedAt"] = inv["issuedAt"],
                ["dueAt"] = inv["dueAt"],
                ["paidAt"] = inv["paidAt"],
                ["arStatus"] = DeriveArStatus(balance, dueAt),
                ["payments"] = paymentsByInvoice.TryGetValue(id, out var pl) ? pl : new List<Dictionary<string, object?>>(),
            };
            result.Add(view);
        }
        return result;
    }

    // One invoice, with the line detail and tax breakdown a customer needs to check
    // what they are being charged for — the thing the summary card could never show.
    // Scoped by company_id AND customer_id exactly like every other portal read, so a
    // guessed id belonging to another customer resolves to null, not to their data.
    public async Task<Dictionary<string, object?>?> GetOwnInvoiceDetailAsync(
        long companyId, long customerId, Guid invoiceId, CancellationToken ct = default)
    {
        var inv = await db.QuerySingleAsync(
            @"SELECT i.id, i.invoice_number, i.currency, i.subtotal, i.tax_total, i.total,
                     i.amount_paid, i.balance_due, i.payment_status, i.issued_at, i.due_at, i.paid_at,
                     c.name AS customer_name,
                     cts.tax_registration_no AS customer_tax_id,
                     co.name AS seller_name, co.legal_name AS seller_legal_name
              FROM issued_invoices i
              JOIN customers c ON c.id = i.customer_id AND c.company_id = i.company_id
              JOIN companies co ON co.id = i.company_id
              LEFT JOIN LATERAL (
                  SELECT tax_registration_no
                  FROM customer_tax_status s
                  WHERE s.company_id = i.company_id AND s.customer_id = i.customer_id
                    AND s.effective_date <= CURRENT_DATE
                    AND (s.expiry_date IS NULL OR s.expiry_date >= CURRENT_DATE)
                  ORDER BY s.effective_date DESC
                  LIMIT 1
              ) cts ON TRUE
              WHERE i.company_id=@companyId AND i.customer_id=@customerId AND i.id=@invoiceId",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@invoiceId", invoiceId);
            }, ct);
        if (inv is null) return null;

        var lines = await db.QueryAsync(
            @"SELECT line_no, description, charge_code, quantity, unit, unit_rate, amount
              FROM issued_invoice_lines
              WHERE company_id=@companyId AND issued_invoice_id=@invoiceId
              ORDER BY line_no",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@invoiceId", invoiceId);
            }, ct);

        // Grouped by rate — the summary block a VAT/GST invoice must print, and what
        // a customer's AP team reconciles against.
        var taxBreakdown = await db.QueryAsync(
            @"SELECT tax_code, tax_category, jurisdiction, rate,
                     SUM(taxable_amount) AS taxable_amount, SUM(tax_amount) AS tax_amount
              FROM issued_invoice_tax_lines
              WHERE company_id=@companyId AND issued_invoice_id=@invoiceId
              GROUP BY tax_code, tax_category, jurisdiction, rate
              ORDER BY rate DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@invoiceId", invoiceId);
            }, ct);

        var payments = await db.QueryAsync(
            @"SELECT payment_reference, amount, currency, payment_method, received_at
              FROM invoice_payments
              WHERE company_id=@companyId AND issued_invoice_id=@invoiceId
              ORDER BY received_at",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@invoiceId", invoiceId);
            }, ct);

        // The seller's own tax registration, so the document carries a real
        // registration number rather than a blank statutory field.
        var sellerReg = await db.QuerySingleAsync(
            @"SELECT tax_registration_no, regime, jurisdiction, legal_name
              FROM seller_tax_registration
              WHERE company_id=@companyId AND (expiry_date IS NULL OR expiry_date >= CURRENT_DATE)
              ORDER BY effective_date DESC LIMIT 1",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);

        return new Dictionary<string, object?>
        {
            ["id"] = inv["id"],
            ["invoiceNumber"] = inv["invoiceNumber"],
            ["currency"] = inv["currency"],
            ["subtotal"] = inv["subtotal"],
            ["taxTotal"] = inv["taxTotal"],
            ["total"] = inv["total"],
            ["amountPaid"] = inv["amountPaid"],
            ["balanceDue"] = inv["balanceDue"],
            ["issuedAt"] = inv["issuedAt"],
            ["dueAt"] = inv["dueAt"],
            ["paidAt"] = inv["paidAt"],
            ["arStatus"] = DeriveArStatus(Dec(inv, "balanceDue"), DtoN(inv, "dueAt")),
            ["customerName"] = inv["customerName"],
            ["customerTaxId"] = inv["customerTaxId"],
            ["sellerName"] = inv["sellerLegalName"] ?? inv["sellerName"],
            ["sellerTaxRegistrationNo"] = sellerReg?["taxRegistrationNo"],
            ["sellerTaxRegime"] = sellerReg?["regime"],
            ["placeOfSupply"] = sellerReg?["jurisdiction"],
            ["lines"] = lines,
            ["taxBreakdown"] = taxBreakdown,
            ["payments"] = payments,
        };
    }

    // Plain-English AR status — the portal shows this, never raw aging-bucket jargon.
    public static string DeriveArStatus(decimal balanceDue, DateTimeOffset? dueAt)
    {
        if (balanceDue <= 0) return "Paid";
        if (dueAt is null) return "Outstanding";
        var days = (int)Math.Ceiling((dueAt.Value - DateTimeOffset.UtcNow).TotalDays);
        return days >= 0 ? $"Due in {days} day(s)" : $"Overdue {Math.Abs(days)} day(s)";
    }

    // Own jobs — customer-safe status/timing only. STRIPS risk_score, cost/margin/revenue
    // estimates, dispatcher notes, assigned driver identity.
    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetOwnJobsAsync(long companyId, long customerId, CancellationToken ct = default)
    {
        return await db.QueryAsync(
            @"SELECT id, COALESCE(job_number, job_code) AS job_number, status, sla_status,
                     scheduled_start, scheduled_end, pickup_address, dropoff_address, tracking_code, eta
              FROM jobs
              WHERE company_id=@companyId AND customer_id=@customerId AND deleted_at IS NULL
              ORDER BY scheduled_start DESC NULLS LAST, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            }, ct);
    }

    // Own job detail (safe fields) + a simple status timeline + proofs. Returns null if
    // the job does not belong to this customer (ownership enforced in the query).
    public async Task<Dictionary<string, object?>?> GetOwnJobDetailAsync(long companyId, long customerId, long jobId, CancellationToken ct = default)
    {
        var job = await db.QuerySingleAsync(
            @"SELECT id, COALESCE(job_number, job_code) AS job_number, status, sla_status,
                     scheduled_start, scheduled_end, pickup_address, dropoff_address, tracking_code, eta
              FROM jobs
              WHERE company_id=@companyId AND customer_id=@customerId AND id=@jobId AND deleted_at IS NULL
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@jobId", jobId);
            }, ct);
        if (job is null) return null;

        var proofs = await GetOwnProofsAsync(companyId, customerId, jobId, ct);
        return new Dictionary<string, object?>
        {
            ["job"] = job,
            ["statusTimeline"] = new object[]
            {
                new { stage = "Scheduled", at = job["scheduledStart"] },
                new { stage = "Current", status = job["status"], eta = job["eta"] },
                new { stage = "Delivery window", at = job["scheduledEnd"] },
            },
            ["proofs"] = proofs,
        };
    }

    // Own proof-of-delivery — customer-safe artifacts (photo/signature refs, timing,
    // capture geo, receiver). STRIPS captured_by_user_id, device_id, validation_summary,
    // correlation/causation, metadata, internal notes. Ownership enforced via the job.
    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetOwnProofsAsync(long companyId, long customerId, long jobId, CancellationToken ct = default)
    {
        var owns = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM jobs WHERE company_id=@companyId AND customer_id=@customerId AND id=@jobId AND deleted_at IS NULL",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@jobId", jobId);
            }, ct);
        if (owns == 0) return new List<Dictionary<string, object?>>();

        var packages = await db.QueryAsync(
            @"SELECT pp.id, pp.proof_type, pp.status, pp.completed_at, pp.receiver_name,
                     pp.receiver_signature_file_id, pp.geo_latitude, pp.geo_longitude
              FROM proof_packages pp
              WHERE pp.company_id=@companyId AND pp.job_id=@jobId
              ORDER BY pp.completed_at NULLS LAST, pp.id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", jobId);
            }, ct);

        var result = new List<Dictionary<string, object?>>();
        foreach (var pp in packages)
        {
            var packageId = Convert.ToInt64(pp["id"], CultureInfo.InvariantCulture);
            var artifacts = await db.QueryAsync(
                @"SELECT artifact_type, file_id, captured_at, geo_latitude, geo_longitude
                  FROM proof_artifacts
                  WHERE company_id=@companyId AND proof_package_id=@packageId
                  ORDER BY captured_at NULLS LAST, id",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@packageId", packageId);
                }, ct);
            result.Add(new Dictionary<string, object?>
            {
                ["proofType"] = pp["proofType"],
                ["status"] = pp["status"],
                ["completedAt"] = pp["completedAt"],
                ["receiverName"] = pp["receiverName"],
                ["signatureFileId"] = pp["receiverSignatureFileId"],
                ["geoLatitude"] = pp["geoLatitude"],
                ["geoLongitude"] = pp["geoLongitude"],
                ["artifacts"] = artifacts,
            });
        }
        return result;
    }

    // Feedback / complaint intake tied to a job the customer owns. Returns null if the
    // job is not theirs. Lifecycle starts at 'open'.
    public async Task<Dictionary<string, object?>?> SubmitFeedbackAsync(
        long companyId, long customerId, long jobId, int? rating, string? comment, string? feedbackType, string? subject, CancellationToken ct = default)
    {
        var owns = await db.ScalarLongAsync(
            "SELECT COUNT(*) FROM jobs WHERE company_id=@companyId AND customer_id=@customerId AND id=@jobId AND deleted_at IS NULL",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@jobId", jobId);
            }, ct);
        if (owns == 0) return null;

        var id = await db.InsertAsync(
            @"INSERT INTO customer_feedback (company_id, customer_id, job_id, rating, comment, feedback_type, subject, status)
              VALUES (@companyId, @customerId, @jobId, @rating, @comment, @feedbackType, @subject, 'open')",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
                c.Parameters.AddWithValue("@jobId", jobId);
                c.Parameters.AddWithValue("@rating", (object?)rating ?? DBNull.Value);
                c.Parameters.AddWithValue("@comment", (object?)comment ?? DBNull.Value);
                c.Parameters.AddWithValue("@feedbackType", (object?)feedbackType ?? "general");
                c.Parameters.AddWithValue("@subject", (object?)subject ?? DBNull.Value);
            }, ct);

        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["jobId"] = jobId,
            ["rating"] = rating,
            ["comment"] = comment,
            ["feedbackType"] = feedbackType ?? "general",
            ["subject"] = subject,
            ["status"] = "open",
        };
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> GetOwnFeedbackAsync(long companyId, long customerId, CancellationToken ct = default)
    {
        return await db.QueryAsync(
            @"SELECT id, job_id, rating, comment, feedback_type, subject, status, created_at
              FROM customer_feedback
              WHERE company_id=@companyId AND customer_id=@customerId
              ORDER BY created_at DESC, id DESC",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            }, ct);
    }

    private static decimal Dec(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : 0m;
    private static DateTimeOffset? DtoN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull
            ? (value is DateTimeOffset dto ? dto : new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture), TimeSpan.Zero))
            : null;
}
