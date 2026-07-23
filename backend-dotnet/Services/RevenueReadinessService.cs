using System.Globalization;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

public sealed record InvoiceDraftLineRecord(
    Guid Id,
    long CompanyId,
    Guid InvoiceDraftId,
    long? JobChargeId,
    int LineNo,
    string Description,
    string? ChargeCode,
    decimal Quantity,
    string? Unit,
    decimal UnitRate,
    decimal Amount,
    string? MetadataJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record InvoiceDraftRecord(
    Guid Id,
    long CompanyId,
    long CustomerId,
    long? ContractId,
    long? JobId,
    string InvoiceDraftNo,
    string Status,
    string Currency,
    decimal Subtotal,
    decimal TaxTotal,
    decimal Total,
    string Source,
    string? MetadataJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    Guid? CreatedBy = null,
    Guid? UpdatedBy = null,
    long? ApprovalRequestId = null,
    IReadOnlyList<InvoiceDraftLineRecord>? Lines = null);

public sealed record ReadyToBillOutcome(
    bool Success,
    string Message,
    long JobId,
    string JobStatus,
    bool RecommendationCreated,
    long? RecommendationId = null);

public sealed record InvoiceDraftActionOutcome(
    bool Success,
    string Message,
    bool ApprovalRequired = false,
    long? ApprovalRequestId = null,
    bool Replay = false,
    InvoiceDraftRecord? Draft = null);

public sealed record IssuedInvoiceLineRecord(
    Guid Id,
    long CompanyId,
    Guid IssuedInvoiceId,
    Guid? SourceInvoiceDraftLineId,
    long? JobChargeId,
    int LineNo,
    string Description,
    string? ChargeCode,
    decimal Quantity,
    string? Unit,
    decimal UnitRate,
    decimal Amount,
    string? MetadataJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record IssuedInvoiceRecord(
    Guid Id,
    long CompanyId,
    Guid SourceInvoiceDraftId,
    long CustomerId,
    long? ContractId,
    long? JobId,
    long? ApprovalRequestId,
    string InvoiceNumber,
    string Status,
    string Currency,
    decimal Subtotal,
    decimal TaxTotal,
    decimal Total,
    decimal AmountPaid,
    decimal BalanceDue,
    string PaymentStatus,
    DateTimeOffset IssuedAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? PaidAt,
    string? IssuedByActorType,
    string? IssuedByActorId,
    string? CorrelationId,
    string? CausationId,
    string? IdempotencyKey,
    string? MetadataJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<IssuedInvoiceLineRecord>? Lines = null);

public sealed record InvoiceIssueOutcome(
    bool Success,
    string Message,
    bool ApprovalRequired = false,
    long? ApprovalRequestId = null,
    bool Replay = false,
    IssuedInvoiceRecord? Invoice = null);

public sealed record InvoicePaymentRecord(
    long Id,
    long CompanyId,
    Guid IssuedInvoiceId,
    string PaymentReference,
    string PaymentMethod,
    string Currency,
    decimal Amount,
    DateTimeOffset ReceivedAt,
    string Status,
    string? MetadataJson,
    string? CorrelationId,
    string? CausationId,
    DateTimeOffset CreatedAt);

public sealed record AccountsReceivableSummaryRecord(
    long CompanyId,
    long IssuedInvoiceCount,
    long OpenInvoiceCount,
    decimal OpenBalance,
    decimal DueSoonBalance,
    decimal PastDueBalance,
    decimal PaidBalance,
    string Currency);

public sealed record RevenueSummaryRecord(
    long CompanyId,
    decimal TotalDraftCharges,
    long ReadyToBillJobsCount,
    long InvoiceDraftsCount,
    decimal InvoiceDraftTotal,
    long JobsMissingChargesCount,
    long JobsMissingContractOrRateCardCount,
    long RevenueLeakageRiskCount,
    string Currency,
    IReadOnlyList<Dictionary<string, object?>> TopCustomersByDraftRevenue);

public sealed record CustomerSummaryRecord(
    long CompanyId,
    long CustomerId,
    Dictionary<string, object?> Customer,
    long ActiveContractsCount,
    long JobsCount,
    long CompletedJobsCount,
    long ReadyToBillJobsCount,
    long InvoiceDraftsCount,
    decimal InvoiceDraftTotal,
    long OpenRevenueLeakageRecommendationsCount,
    IReadOnlyList<Dictionary<string, object?>> RecentJobs,
    IReadOnlyList<Dictionary<string, object?>> RecentCharges,
    IReadOnlyList<Dictionary<string, object?>> RecentInvoiceDrafts);

// ── AR aging (Finance completion) ──────────────────────────────────────────────
public sealed record ArAgingCustomerRecord(
    long CustomerId,
    string CustomerName,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Days90Plus,
    decimal TotalOutstanding);

public sealed record ArAgingRecord(
    long CompanyId,
    string Currency,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Days90Plus,
    decimal TotalOutstanding,
    IReadOnlyList<ArAgingCustomerRecord> Customers);

// ── Payment summary (Finance completion) ───────────────────────────────────────
public sealed record PaymentSummaryCustomerRecord(
    long CustomerId,
    string CustomerName,
    decimal TotalCollected,
    decimal TotalOutstanding,
    decimal? AverageDaysToPay,
    long PaidInvoiceCount);

public sealed record PaymentSummaryRecord(
    long CompanyId,
    string Currency,
    DateTimeOffset FromDate,
    DateTimeOffset ToDate,
    decimal TotalCollected,
    decimal TotalOutstanding,
    decimal? AverageDaysToPay,
    long PaymentCount,
    long PaidInvoiceCount,
    IReadOnlyList<PaymentSummaryCustomerRecord> Customers);

// ── Revenue leakage signals (Finance completion; persisted to cost_leakage_items) ─
public sealed record RevenueLeakageSignalRecord(
    long Id,
    string LeakageNumber,
    string SignalType,
    string EntityType,
    long EntityId,
    decimal DetectedAmount,
    string Severity,
    string Status,
    string Title);

public sealed record RevenueLeakageDetectionOutcome(
    long CompanyId,
    int SignalsCreated,
    int SignalsAlreadyOpen,
    IReadOnlyList<RevenueLeakageSignalRecord> Signals);

public sealed class RevenueReadinessService(
    Database db,
    PostgresAiFoundationService ai,
    IApprovalWorkflowService approval,
    IEventIdempotencyService idempotency,
    IDomainEventPublisher events,
    ICorrelationContext correlation,
    TaxService tax)
{
    public async Task<ReadyToBillOutcome> MarkJobReadyToBillAsync(long companyId, long jobId, CancellationToken ct = default)
    {
        var job = await LoadJobAsync(companyId, jobId, ct);
        if (job is null)
        {
            return new ReadyToBillOutcome(false, "Job not found", jobId, "missing", false);
        }

        if (!IsEligibleReadyStatus(job.Status))
        {
            return new ReadyToBillOutcome(false, $"Job status {job.Status} is not ready to bill", jobId, job.Status, false);
        }

        var charges = await LoadJobChargesAsync(companyId, jobId, ct);
        if (charges.Count == 0)
        {
            var recommendationCreated = await EnsureLeakageRecommendationAsync(
                companyId,
                jobId,
                "completed_job_missing_charges",
                "Completed job missing charges",
                $"Job {job.JobCode} is completed but has no job charges.",
                "revenue.leakage_detected",
                "high",
                "{\"missing\":\"job_charges\"}",
                "{\"reason\":\"Completed job has no billable charges\"}",
                "{\"action\":\"draft_missing_charge_review\"}",
                "draft_missing_charge_review",
                ct);

            return new ReadyToBillOutcome(false, "Job is missing charges and was not marked ready to bill", jobId, job.Status, recommendationCreated, null);
        }

        if (job.ContractId is null || charges.All(charge => charge.RateCardId is null))
        {
            await EnsureLeakageRecommendationAsync(
                companyId,
                jobId,
                "job_without_contract_or_rate_card",
                "Job missing contract or rate card",
                $"Job {job.JobCode} is billable but still missing contract or rate card coverage.",
                "revenue.leakage_detected",
                "medium",
                JsonSerializer.Serialize(new { jobId, jobCode = job.JobCode, contractId = job.ContractId, rateCardId = job.RateCardId }),
                "{\"reason\":\"Pricing foundation missing\"}",
                "{\"action\":\"review_pricing_setup\"}",
                "review_pricing_setup",
                ct);
        }

        var current = await db.QuerySingleAsync(
            @"UPDATE jobs
              SET status='ready_to_bill', updated_at=NOW()
              WHERE company_id=@companyId AND id=@jobId
              RETURNING id, company_id, customer_id, contract_id, rate_card_id, job_code, status",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", jobId);
            },
            ct);

        if (current is null)
        {
            return new ReadyToBillOutcome(false, "Job not found for update", jobId, job.Status, false);
        }

        var payload = JsonSerializer.Serialize(new
        {
            jobId = current["id"],
            companyId = current["companyId"],
            jobCode = current["jobCode"],
            previousStatus = job.Status,
            status = "ready_to_bill",
            chargeCount = charges.Count,
        });

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "job.ready_to_bill",
            "job",
            jobId.ToString(CultureInfo.InvariantCulture),
            payload,
            correlation.CorrelationId,
            correlation.CausationId,
            $"job.ready_to_bill:{jobId}");

        return new ReadyToBillOutcome(true, "Job marked ready to bill", jobId, "ready_to_bill", false);
    }

    public async Task<InvoiceDraftActionOutcome> CreateInvoiceDraftFromJobAsync(long companyId, long jobId, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var job = await LoadJobAsync(companyId, jobId, ct);
        if (job is null)
        {
            return new InvoiceDraftActionOutcome(false, "Job not found");
        }

        if (!IsEligibleReadyStatus(job.Status) && !string.Equals(job.Status, "ready_to_bill", StringComparison.OrdinalIgnoreCase))
        {
            return new InvoiceDraftActionOutcome(false, $"Job status {job.Status} is not eligible for invoice drafting");
        }

        var charges = await LoadJobChargesAsync(companyId, jobId, ct);
        if (charges.Count == 0)
        {
            await EnsureLeakageRecommendationAsync(
                companyId,
                jobId,
                "completed_job_missing_charges",
                "Completed job missing charges",
                $"Job {job.JobCode} cannot be drafted because no charges were found.",
                "revenue.leakage_detected",
                "high",
                "{\"missing\":\"job_charges\"}",
                "{\"reason\":\"Invoice draft blocked by missing charges\"}",
                "{\"action\":\"draft_missing_charge_review\"}",
                "draft_missing_charge_review",
                ct);
            return new InvoiceDraftActionOutcome(false, "Job has no charges to draft");
        }

        var requestHash = FoundationPersistenceHelpers.ComputeHash($"{companyId}:{jobId}:{idempotencyKey ?? string.Empty}:{charges.Count}:{charges.Sum(c => c.Amount):0.00}");
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var reservation = idempotency.Reserve(
                companyId.ToString(CultureInfo.InvariantCulture),
                "invoice_draft.create",
                idempotencyKey!,
                requestHash,
                TimeSpan.FromHours(24));

            if (string.Equals(reservation.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(reservation.ResponseReference) &&
                Guid.TryParse(reservation.ResponseReference, out var completedDraftId))
            {
                var existingDraft = await GetInvoiceDraftAsync(companyId, completedDraftId, ct);
                if (existingDraft is not null)
                {
                    return new InvoiceDraftActionOutcome(true, "Invoice draft replayed from idempotency key", Replay: true, Draft: existingDraft);
                }
            }
        }

        var activeDraft = await LoadActiveInvoiceDraftForJobAsync(companyId, jobId, ct);
        if (activeDraft is not null)
        {
            using var metadata = ParseMetadata(activeDraft.MetadataJson);
            var storedKey = metadata.RootElement.TryGetProperty("idempotencyKey", out var keyValue) ? keyValue.GetString() : null;
            var storedHash = metadata.RootElement.TryGetProperty("requestHash", out var hashValue) ? hashValue.GetString() : null;
            if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
                string.Equals(storedKey, idempotencyKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(storedHash, requestHash, StringComparison.OrdinalIgnoreCase))
            {
                var detail = await GetInvoiceDraftAsync(companyId, activeDraft.Id, ct);
                if (detail is not null)
                {
                    return new InvoiceDraftActionOutcome(true, "Invoice draft replayed from active draft", Replay: true, Draft: detail);
                }
            }

            return new InvoiceDraftActionOutcome(false, "An active invoice draft already exists for this job");
        }

        if (job.ContractId is null || charges.All(charge => charge.RateCardId is null))
        {
            await EnsureLeakageRecommendationAsync(
                companyId,
                jobId,
                "job_without_contract_or_rate_card",
                "Job missing contract or rate card",
                $"Job {job.JobCode} has charges but pricing coverage is incomplete.",
                "revenue.leakage_detected",
                "medium",
                JsonSerializer.Serialize(new { jobId, jobCode = job.JobCode, contractId = job.ContractId, rateCardId = job.RateCardId }),
                "{\"reason\":\"Missing contract or rate card\"}",
                "{\"action\":\"review_pricing_setup\"}",
                "review_pricing_setup",
                ct);
        }

        var draftId = Guid.NewGuid();
        var invoiceDraftNo = BuildInvoiceDraftNumber(companyId, jobId);
        var currency = charges.Select(charge => charge.Currency).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "USD";
        var subtotal = charges.Sum(charge => charge.Amount);
        var now = DateTimeOffset.UtcNow;
        var metadataJson = JsonSerializer.Serialize(new
        {
            idempotencyKey,
            requestHash,
            source = "job_charges",
            chargeCount = charges.Count,
        });

        await db.ExecuteAsync(
            @"INSERT INTO invoice_drafts
                (id, company_id, customer_id, contract_id, job_id, invoice_draft_no, status, currency, subtotal, tax_total, total, source, metadata_json, created_at)
              VALUES
                (@id, @companyId, @customerId, @contractId, @jobId, @invoiceDraftNo, 'draft', @currency, @subtotal, 0, @total, 'job_charges', @metadata::jsonb, @createdAt)",
            c =>
            {
                c.Parameters.AddWithValue("@id", draftId);
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", job.CustomerId ?? throw new InvalidOperationException("Job missing customer."));
                c.Parameters.AddWithValue("@contractId", (object?)job.ContractId ?? DBNull.Value);
                c.Parameters.AddWithValue("@jobId", jobId);
                c.Parameters.AddWithValue("@invoiceDraftNo", invoiceDraftNo);
                c.Parameters.AddWithValue("@currency", currency);
                c.Parameters.AddWithValue("@subtotal", subtotal);
                c.Parameters.AddWithValue("@total", subtotal);
                c.Parameters.AddWithValue("@metadata", metadataJson);
                c.Parameters.AddWithValue("@createdAt", now);
            },
            ct);

        var lineNo = 1;
        foreach (var charge in charges)
        {
            await db.ExecuteAsync(
                @"INSERT INTO invoice_draft_lines
                    (id, company_id, invoice_draft_id, job_charge_id, line_no, description, charge_code, quantity, unit, unit_rate, amount, metadata_json, created_at)
                  VALUES
                    (@id, @companyId, @invoiceDraftId, @jobChargeId, @lineNo, @description, @chargeCode, @quantity, @unit, @unitRate, @amount, @metadata::jsonb, @createdAt)",
                c =>
                {
                    c.Parameters.AddWithValue("@id", Guid.NewGuid());
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@invoiceDraftId", draftId);
                    c.Parameters.AddWithValue("@jobChargeId", charge.Id);
                    c.Parameters.AddWithValue("@lineNo", lineNo++);
                    c.Parameters.AddWithValue("@description", charge.Description ?? charge.ChargeName);
                    c.Parameters.AddWithValue("@chargeCode", (object?)charge.ChargeCode ?? DBNull.Value);
                    c.Parameters.AddWithValue("@quantity", charge.Quantity);
                    c.Parameters.AddWithValue("@unit", (object?)charge.ChargeType ?? DBNull.Value);
                    c.Parameters.AddWithValue("@unitRate", charge.UnitRate);
                    c.Parameters.AddWithValue("@amount", charge.Amount);
                    c.Parameters.AddWithValue("@metadata", JsonSerializer.Serialize(new { chargeId = charge.Id, charge.Status }));
                    c.Parameters.AddWithValue("@createdAt", now);
                },
                ct);
        }

        // Tax engine (ADR-008 P3): compute the draft's tax from a published tax_profile and set
        // tax_total/total. Fail-closed — no profile reproduces the literal tax_total=0/total=subtotal
        // written above, byte-for-byte. Idempotent (delete-and-recompute), so re-running is safe.
        _ = await tax.RecalculateDraftTaxAsync(companyId, draftId, ct);

        var draft = await GetInvoiceDraftAsync(companyId, draftId, ct);
        if (draft is null)
        {
            throw new InvalidOperationException("Invoice draft was created but could not be reloaded.");
        }

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "invoice_draft.created",
            "invoice_draft",
            draftId.ToString(),
            JsonSerializer.Serialize(new
            {
                invoiceDraftId = draftId,
                companyId,
                jobId,
                invoiceDraftNo,
                chargeCount = charges.Count,
                // Serialize the RELOADED taxed figures, not the pre-tax local subtotal.
                subtotal = draft.Subtotal,
                taxTotal = draft.TaxTotal,
                total = draft.Total,
                currency
            }),
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _ = idempotency.TryComplete(
                companyId.ToString(CultureInfo.InvariantCulture),
                "invoice_draft.create",
                idempotencyKey!,
                FoundationPersistenceHelpers.ComputeHash(draftId.ToString()),
                draftId.ToString());
        }

        return new InvoiceDraftActionOutcome(true, "Invoice draft created", Draft: draft);
    }

    public async Task<IReadOnlyList<InvoiceDraftRecord>> ListInvoiceDraftsAsync(long companyId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT * FROM invoice_drafts WHERE company_id=@companyId ORDER BY created_at DESC, invoice_draft_no DESC LIMIT 100",
            c => c.Parameters.AddWithValue("@companyId", companyId),
            ct);
        var drafts = rows.Select(row => MapInvoiceDraft(row)).ToList();
        return drafts;
    }

    public async Task<InvoiceDraftRecord?> GetInvoiceDraftAsync(long companyId, Guid draftId, CancellationToken ct = default)
    {
        var draftRow = await db.QuerySingleAsync(
            @"SELECT * FROM invoice_drafts WHERE company_id=@companyId AND id=@id LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", draftId);
            },
            ct);
        if (draftRow is null)
        {
            return null;
        }

        var lines = await db.QueryAsync(
            @"SELECT * FROM invoice_draft_lines WHERE company_id=@companyId AND invoice_draft_id=@id ORDER BY line_no, created_at",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", draftId);
            },
            ct);

        return MapInvoiceDraft(draftRow, lines.Select(MapInvoiceDraftLine).ToList());
    }

    public async Task<InvoiceDraftActionOutcome> UpdateInvoiceDraftAsync(long companyId, Guid draftId, string? status = null, string? metadataJson = null, CancellationToken ct = default)
    {
        var current = await GetInvoiceDraftAsync(companyId, draftId, ct);
        if (current is null)
        {
            return new InvoiceDraftActionOutcome(false, "Invoice draft not found");
        }

        if (string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase))
        {
            if (current.ApprovalRequestId is not null)
            {
                return new InvoiceDraftActionOutcome(false, "Invoice draft approval already requested", true, current.ApprovalRequestId);
            }

            var approvalRequest = approval.CreateRequest(
                companyId.ToString(CultureInfo.InvariantCulture),
                ActorTypes.TenantUser,
                correlation.ActorId,
                "finance.invoice.issue",
                "invoice_draft",
                draftId.ToString(),
                metadataJson ?? JsonSerializer.Serialize(new { invoiceDraftId = draftId, status = "approved" }),
                "high");

            await db.ExecuteAsync(
                @"UPDATE invoice_drafts
                  SET status='pending_review',
                      approval_request_id=@approvalRequestId,
                      updated_at=NOW()
                  WHERE company_id=@companyId AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@id", draftId);
                    c.Parameters.AddWithValue("@approvalRequestId", approvalRequest.Id);
                },
                ct);

            return new InvoiceDraftActionOutcome(false, "Invoice draft approval requires approval", true, approvalRequest.Id);
        }

        await db.ExecuteAsync(
            @"UPDATE invoice_drafts
              SET status=COALESCE(@status, status),
                  metadata_json=COALESCE(@metadata::jsonb, metadata_json),
                  updated_at=NOW()
              WHERE company_id=@companyId AND id=@id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", draftId);
                c.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
                c.Parameters.AddWithValue("@metadata", (object?)metadataJson ?? DBNull.Value);
            },
            ct);

        var updated = await GetInvoiceDraftAsync(companyId, draftId, ct);
        return new InvoiceDraftActionOutcome(true, "Invoice draft updated", Draft: updated ?? current);
    }

    public async Task<InvoiceIssueOutcome> IssueInvoiceFromDraftAsync(long companyId, Guid draftId, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var draft = await GetInvoiceDraftAsync(companyId, draftId, ct);
        if (draft is null)
        {
            return new InvoiceIssueOutcome(false, "Invoice draft not found");
        }

        var existingInvoice = await LoadIssuedInvoiceByDraftAsync(companyId, draftId, ct);
        if (existingInvoice is not null)
        {
            return new InvoiceIssueOutcome(true, "Invoice already issued", Replay: true, Invoice: existingInvoice);
        }

        // POD gate (ADR-008 billing Phase 1): when the tenant enables billing.require_pod_to_issue,
        // a job-linked invoice can't issue without proof of delivery. Flag defaults OFF so existing
        // issuance is unchanged; ad-hoc drafts (no JobId) are exempt. Evaluated live at issue time,
        // so a POD captured after delivery just works on the next attempt.
        if (draft.JobId is long podJobId
            && await IsPodRequiredToIssueAsync(companyId, ct)
            && !await HasDeliveryProofAsync(companyId, podJobId, ct))
        {
            return new InvoiceIssueOutcome(false, $"Cannot issue invoice: no proof of delivery captured for job {podJobId}");
        }

        if (draft.ApprovalRequestId is null)
        {
            var approvalRequest = approval.CreateRequest(
                companyId.ToString(CultureInfo.InvariantCulture),
                ActorTypes.TenantUser,
                correlation.ActorId,
                "finance.invoice.issue",
                "invoice_draft",
                draftId.ToString(),
                JsonSerializer.Serialize(new
                {
                    invoiceDraftId = draftId,
                    invoiceDraftNo = draft.InvoiceDraftNo,
                    total = draft.Total,
                    currency = draft.Currency
                }),
                "high");

            await db.ExecuteAsync(
                @"UPDATE invoice_drafts
                  SET status='pending_review',
                      approval_request_id=@approvalRequestId,
                      updated_at=NOW()
                  WHERE company_id=@companyId AND id=@id",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@id", draftId);
                    c.Parameters.AddWithValue("@approvalRequestId", approvalRequest.Id);
                },
                ct);

            return new InvoiceIssueOutcome(false, "Invoice issue requires approval", true, approvalRequest.Id);
        }

        var approvalStatus = await GetApprovalRequestStatusAsync(companyId, draft.ApprovalRequestId.Value, ct);
        if (!string.Equals(approvalStatus, "approved", StringComparison.OrdinalIgnoreCase))
        {
            var approvalRequired = string.Equals(approvalStatus, "pending", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(approvalStatus, "approval_required", StringComparison.OrdinalIgnoreCase);
            return new InvoiceIssueOutcome(false, approvalStatus is null
                ? "Approval request not found"
                : $"Invoice issue approval is {approvalStatus}", approvalRequired, draft.ApprovalRequestId);
        }

        var requestHash = FoundationPersistenceHelpers.ComputeHash(
            $"{companyId}:{draftId}:{draft.ApprovalRequestId}:{draft.Total:0.00}:{idempotencyKey ?? string.Empty}");
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var reservation = idempotency.Reserve(
                companyId.ToString(CultureInfo.InvariantCulture),
                "invoice.issue",
                idempotencyKey!,
                requestHash,
                TimeSpan.FromHours(24));

            if (string.Equals(reservation.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(reservation.ResponseReference) &&
                Guid.TryParse(reservation.ResponseReference, out var replayId))
            {
                var replay = await GetIssuedInvoiceAsync(companyId, replayId, ct);
                if (replay is not null)
                {
                    return new InvoiceIssueOutcome(true, "Invoice replayed from idempotency key", Replay: true, Invoice: replay);
                }
            }
        }

        IssuedInvoiceRecord issued;
        try
        {
        issued = await db.WithTransactionAsync(async (conn, tx) =>
        {
            // Tax engine (ADR-008 P3): recompute tax at the issue tax point INSIDE this tx so the issued
            // figures and the immutable snapshot can never desync. Hard-gate — an unsupported/blocked
            // computation must never issue; no_tax_profile is the fail-closed zero baseline (allowed).
            var issueDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
            var taxOutcome = await tax.ComputeForDraftInTxAsync(conn, tx, companyId, draftId, issueDate, ct);
            if (!taxOutcome.Applied && taxOutcome.Reason != "no_tax_profile")
                throw new TaxIssueBlockedException(taxOutcome.Reason ?? "tax_blocked");
            var freshSubtotal = taxOutcome.Applied ? taxOutcome.Subtotal : draft.Subtotal;
            var freshTaxTotal = taxOutcome.Applied ? taxOutcome.TaxTotal : 0m;
            var freshTotal = taxOutcome.Applied ? taxOutcome.Total : draft.Subtotal;
            // Re-approval: if tax moved the total away from the approved amount (e.g. a profile published
            // after approval was granted), the approval no longer covers what would be billed — reject.
            if (freshTotal != draft.Total)
                throw new TaxReapprovalException(freshTotal);

            var sourceLines = draft.Lines ?? [];
            var issuedAt = DateTimeOffset.UtcNow;
            var dueAt = issuedAt.AddDays(30);
            var invoiceId = Guid.NewGuid();
            var invoiceNumber = BuildIssuedInvoiceNumber(companyId, draftId);
            var amountPaid = 0m;
            var balanceDue = freshTotal - amountPaid;
            var metadataJson = JsonSerializer.Serialize(new
            {
                sourceInvoiceDraftId = draft.Id,
                sourceInvoiceDraftNo = draft.InvoiceDraftNo,
                approvalRequestId = draft.ApprovalRequestId,
                source = "invoice_draft"
            });
            var issuedLines = sourceLines.Select(line => new IssuedInvoiceLineRecord(
                Guid.NewGuid(),
                companyId,
                invoiceId,
                line.Id,
                line.JobChargeId,
                line.LineNo,
                line.Description,
                line.ChargeCode,
                line.Quantity,
                line.Unit,
                line.UnitRate,
                line.Amount,
                line.MetadataJson,
                issuedAt,
                null)).ToList();

            await using (var insert = new Npgsql.NpgsqlCommand(
                @"INSERT INTO issued_invoices
                    (id, company_id, customer_id, contract_id, job_id, approval_request_id, source_invoice_draft_id, source_invoice_draft_no,
                     invoice_number, status, currency, subtotal, tax_total, total, amount_paid, balance_due, payment_status,
                     issued_at, due_at, issued_by_actor_type, issued_by_actor_id, correlation_id, causation_id, idempotency_key, metadata_json, created_at,
                     tax_profile_id, tax_point_date)
                  VALUES
                    (@id, @companyId, @customerId, @contractId, @jobId, @approvalRequestId, @sourceDraftId, @sourceDraftNo,
                     @invoiceNumber, 'issued', @currency, @subtotal, @taxTotal, @total, @amountPaid, @balanceDue, @paymentStatus,
                     @issuedAt, @dueAt, @issuedByActorType, @issuedByActorId, @correlationId, @causationId, @idempotencyKey, @metadata::jsonb, @createdAt,
                     @taxProfileId, @taxPointDate)
                  RETURNING id", conn, tx))
            {
                insert.Parameters.AddWithValue("@id", invoiceId);
                insert.Parameters.AddWithValue("@companyId", companyId);
                insert.Parameters.AddWithValue("@customerId", draft.CustomerId);
                insert.Parameters.AddWithValue("@contractId", (object?)draft.ContractId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@jobId", (object?)draft.JobId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@approvalRequestId", (object?)draft.ApprovalRequestId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@sourceDraftId", draft.Id);
                insert.Parameters.AddWithValue("@sourceDraftNo", draft.InvoiceDraftNo);
                insert.Parameters.AddWithValue("@invoiceNumber", invoiceNumber);
                insert.Parameters.AddWithValue("@currency", draft.Currency);
                insert.Parameters.AddWithValue("@subtotal", freshSubtotal);
                insert.Parameters.AddWithValue("@taxTotal", freshTaxTotal);
                insert.Parameters.AddWithValue("@total", freshTotal);
                insert.Parameters.AddWithValue("@taxProfileId", (object?)taxOutcome.TaxProfileId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@taxPointDate", taxOutcome.Applied ? issueDate : (object)DBNull.Value);
                insert.Parameters.AddWithValue("@amountPaid", amountPaid);
                insert.Parameters.AddWithValue("@balanceDue", balanceDue);
                insert.Parameters.AddWithValue("@paymentStatus", balanceDue <= 0 ? "paid" : "unpaid");
                insert.Parameters.AddWithValue("@issuedAt", issuedAt);
                insert.Parameters.AddWithValue("@dueAt", dueAt);
                insert.Parameters.AddWithValue("@issuedByActorType", correlation.ActorType ?? ActorTypes.System);
                insert.Parameters.AddWithValue("@issuedByActorId", (object?)correlation.ActorId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@correlationId", (object?)correlation.CorrelationId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@causationId", (object?)correlation.CausationId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@idempotencyKey", (object?)idempotencyKey ?? DBNull.Value);
                insert.Parameters.AddWithValue("@metadata", metadataJson);
                insert.Parameters.AddWithValue("@createdAt", issuedAt);
                _ = await insert.ExecuteScalarAsync(ct);
            }

            var lineNo = 1;
            foreach (var line in issuedLines)
            {
                await using var lineInsert = new Npgsql.NpgsqlCommand(
                    @"INSERT INTO issued_invoice_lines
                        (id, company_id, issued_invoice_id, source_invoice_draft_line_id, job_charge_id, line_no, description, charge_code, quantity, unit, unit_rate, amount, metadata_json, created_at)
                      VALUES
                        (@id, @companyId, @issuedInvoiceId, @sourceDraftLineId, @jobChargeId, @lineNo, @description, @chargeCode, @quantity, @unit, @unitRate, @amount, @metadata::jsonb, @createdAt)",
                    conn, tx);
                lineInsert.Parameters.AddWithValue("@id", line.Id);
                lineInsert.Parameters.AddWithValue("@companyId", companyId);
                lineInsert.Parameters.AddWithValue("@issuedInvoiceId", invoiceId);
                lineInsert.Parameters.AddWithValue("@sourceDraftLineId", line.SourceInvoiceDraftLineId ?? Guid.Empty);
                lineInsert.Parameters.AddWithValue("@jobChargeId", (object?)line.JobChargeId ?? DBNull.Value);
                lineInsert.Parameters.AddWithValue("@lineNo", lineNo++);
                lineInsert.Parameters.AddWithValue("@description", line.Description);
                lineInsert.Parameters.AddWithValue("@chargeCode", (object?)line.ChargeCode ?? DBNull.Value);
                lineInsert.Parameters.AddWithValue("@quantity", line.Quantity);
                lineInsert.Parameters.AddWithValue("@unit", (object?)line.Unit ?? DBNull.Value);
                lineInsert.Parameters.AddWithValue("@unitRate", line.UnitRate);
                lineInsert.Parameters.AddWithValue("@amount", line.Amount);
                lineInsert.Parameters.AddWithValue("@metadata", line.MetadataJson ?? "{}");
                lineInsert.Parameters.AddWithValue("@createdAt", issuedAt);
                await lineInsert.ExecuteNonQueryAsync(ct);
            }

            // Copy the mutable tax breakdown into the immutable issued snapshot (asserts it still foots
            // to the draft tax_total). Append-only — corrections are reversing credit notes, never edits.
            await tax.SnapshotIssuedTaxLinesAsync(conn, tx, companyId, draftId, invoiceId, ct);
            // NB: the post-commit events.Publish("invoice.issued") below already enqueues the durable
            // outbox event that drives the rev-rec sub-ledger (derive-beside) — no explicit enqueue here.

            await using (var draftUpdate = new Npgsql.NpgsqlCommand(
                @"UPDATE invoice_drafts
                  SET status='issued',
                      updated_at=NOW()
                  WHERE company_id=@companyId AND id=@id",
                conn, tx))
            {
                draftUpdate.Parameters.AddWithValue("@companyId", companyId);
                draftUpdate.Parameters.AddWithValue("@id", draftId);
                await draftUpdate.ExecuteNonQueryAsync(ct);
            }

            return new IssuedInvoiceRecord(
                invoiceId,
                companyId,
                draft.Id,
                draft.CustomerId,
                draft.ContractId,
                draft.JobId,
                draft.ApprovalRequestId,
                invoiceNumber,
                "issued",
                draft.Currency,
                freshSubtotal,
                freshTaxTotal,
                freshTotal,
                amountPaid,
                balanceDue,
                balanceDue <= 0 ? "paid" : "unpaid",
                issuedAt,
                dueAt,
                null,
                correlation.ActorType ?? ActorTypes.System,
                correlation.ActorId,
                correlation.CorrelationId,
                correlation.CausationId,
                idempotencyKey,
                metadataJson,
                issuedAt,
                null,
                issuedLines);
        }, ct);
        }
        catch (TaxReapprovalException reapproval)
        {
            // Tax changed the billable total after approval — the granted approval is void. Reset the
            // draft to pending_review and open a fresh approval request for the new amount.
            var newRequest = approval.CreateRequest(
                companyId.ToString(CultureInfo.InvariantCulture), ActorTypes.TenantUser, correlation.ActorId,
                "finance.invoice.issue", "invoice_draft", draftId.ToString(),
                JsonSerializer.Serialize(new { invoiceDraftId = draftId, invoiceDraftNo = draft.InvoiceDraftNo, total = reapproval.NewTotal, currency = draft.Currency }),
                "high");
            await db.ExecuteAsync(
                @"UPDATE invoice_drafts SET status='pending_review', approval_request_id=@r, updated_at=NOW()
                  WHERE company_id=@c AND id=@id",
                c => { c.Parameters.AddWithValue("@r", newRequest.Id); c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@id", draftId); }, ct);
            return new InvoiceIssueOutcome(false, "Tax changed since approval — re-approval required", true, newRequest.Id);
        }
        catch (TaxIssueBlockedException blocked)
        {
            return new InvoiceIssueOutcome(false, $"Cannot issue invoice: tax computation blocked ({blocked.Reason})");
        }

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "invoice.issued",
            "issued_invoice",
            issued.Id.ToString(),
            JsonSerializer.Serialize(new
            {
                issuedInvoiceId = issued.Id,
                sourceInvoiceDraftId = draftId,
                issued.InvoiceNumber,
                issued.Total,
                issued.Currency,
                issued.PaymentStatus
            }),
            correlation.CorrelationId,
            correlation.CausationId,
            idempotencyKey);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            _ = idempotency.TryComplete(
                companyId.ToString(CultureInfo.InvariantCulture),
                "invoice.issue",
                idempotencyKey!,
                FoundationPersistenceHelpers.ComputeHash(issued.Id.ToString()),
                issued.Id.ToString());
        }

        return new InvoiceIssueOutcome(true, "Invoice issued", Invoice: issued);
    }

    public async Task<IReadOnlyList<IssuedInvoiceRecord>> ListIssuedInvoicesAsync(long companyId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            @"SELECT * FROM issued_invoices WHERE company_id=@companyId ORDER BY issued_at DESC, invoice_number DESC LIMIT 100",
            c => c.Parameters.AddWithValue("@companyId", companyId),
            ct);
        // Load line items SEQUENTIALLY, not via Task.WhenAll: under RLS enforcement all
        // queries share one request-scoped connection/transaction, so concurrent commands
        // throw "a command is already in progress". Sequential await is correct + safe.
        var result = new List<IssuedInvoiceRecord>(rows.Count);
        foreach (var row in rows)
        {
            var lines = await LoadIssuedInvoiceLinesAsync(companyId, G(row, "id"), ct);
            result.Add(MapIssuedInvoice(row, lines));
        }
        return result;
    }

    public async Task<IssuedInvoiceRecord?> GetIssuedInvoiceAsync(long companyId, Guid invoiceId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT * FROM issued_invoices WHERE company_id=@companyId AND id=@id LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", invoiceId);
            },
            ct);
        if (row is null)
        {
            return null;
        }

        var lines = await LoadIssuedInvoiceLinesAsync(companyId, invoiceId, ct);
        return MapIssuedInvoice(row, lines);
    }

    public async Task<InvoicePaymentRecord?> RecordInvoicePaymentAsync(
        long companyId,
        Guid invoiceId,
        decimal amount,
        string currency,
        string paymentReference,
        string paymentMethod = "manual",
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        var payment = await db.WithTransactionAsync(async (conn, tx) =>
        {
            var invoice = await LoadIssuedInvoiceByIdAsync(conn, tx, companyId, invoiceId, ct);
            if (invoice is null)
            {
                return null;
            }

            var receivedAt = DateTimeOffset.UtcNow;
            var paymentRow = await InsertInvoicePaymentAsync(conn, tx, companyId, invoiceId, amount, currency, paymentReference, paymentMethod, metadataJson, correlation.CorrelationId, correlation.CausationId, receivedAt, ct);

            var newAmountPaid = invoice.AmountPaid + amount;
            // Balance derives from total - paid - CREDITED. Without subtracting credit_total, recording
            // a payment would silently resurrect balance that a credit note already relieved.
            decimal creditTotal;
            await using (var creditCmd = new Npgsql.NpgsqlCommand(
                "SELECT credit_total FROM issued_invoices WHERE id=@id AND company_id=@companyId", conn, tx))
            {
                creditCmd.Parameters.AddWithValue("@id", invoiceId);
                creditCmd.Parameters.AddWithValue("@companyId", companyId);
                creditTotal = Convert.ToDecimal(await creditCmd.ExecuteScalarAsync(ct) ?? 0m);
            }
            var newBalance = invoice.Total - creditTotal - newAmountPaid;
            var paymentStatus = newBalance <= 0 ? "paid" : "partial";

            await using (var update = new Npgsql.NpgsqlCommand(
                @"UPDATE issued_invoices
                  SET amount_paid=@amountPaid,
                      balance_due=@balanceDue,
                      payment_status=@paymentStatus,
                      paid_at=CASE WHEN @paymentStatus='paid' THEN COALESCE(paid_at, @receivedAt) ELSE paid_at END,
                      status=CASE WHEN @paymentStatus='paid' THEN 'paid' ELSE status END,
                      updated_at=NOW()
                  WHERE id=@id AND company_id=@companyId",
                conn, tx))
            {
                update.Parameters.AddWithValue("@amountPaid", newAmountPaid);
                update.Parameters.AddWithValue("@balanceDue", Math.Max(0m, newBalance));
                update.Parameters.AddWithValue("@paymentStatus", paymentStatus);
                update.Parameters.AddWithValue("@receivedAt", receivedAt);
                update.Parameters.AddWithValue("@id", invoiceId);
                update.Parameters.AddWithValue("@companyId", companyId);
                await update.ExecuteNonQueryAsync(ct);
            }

            return paymentRow;
        }, ct);

        if (payment is null)
        {
            return null;
        }

        _ = events.Publish(
            companyId.ToString(CultureInfo.InvariantCulture),
            "invoice.payment.recorded",
            "issued_invoice",
            invoiceId.ToString(),
            JsonSerializer.Serialize(new
            {
                invoiceId,
                payment.Id,
                payment.Amount,
                payment.Currency,
                payment.PaymentReference,
                payment.PaymentMethod
            }),
            correlation.CorrelationId,
            correlation.CausationId,
            paymentReference);

        return payment;
    }

    public async Task<AccountsReceivableSummaryRecord> GetAccountsReceivableSummaryAsync(long companyId, CancellationToken ct = default)
    {
        var summary = await db.QuerySingleAsync(
            @"SELECT
                COUNT(*) AS issued_invoice_count,
                COALESCE(SUM(CASE WHEN balance_due > 0 THEN 1 ELSE 0 END), 0) AS open_invoice_count,
                COALESCE(SUM(balance_due), 0) AS open_balance,
                COALESCE(SUM(CASE WHEN due_at <= NOW() AND balance_due > 0 THEN balance_due ELSE 0 END), 0) AS past_due_balance,
                COALESCE(SUM(CASE WHEN due_at > NOW() AND due_at <= NOW() + INTERVAL '30 days' AND balance_due > 0 THEN balance_due ELSE 0 END), 0) AS due_soon_balance,
                COALESCE(SUM(CASE WHEN payment_status='paid' THEN total ELSE 0 END), 0) AS paid_balance,
                COALESCE((SELECT currency FROM issued_invoices WHERE company_id=@companyId ORDER BY issued_at DESC LIMIT 1), 'USD') AS currency
              FROM issued_invoices
              WHERE company_id=@companyId",
            c => c.Parameters.AddWithValue("@companyId", companyId),
            ct);

        return new AccountsReceivableSummaryRecord(
            companyId,
            L(summary, "issuedInvoiceCount"),
            L(summary, "openInvoiceCount"),
            Dec(summary, "openBalance"),
            Dec(summary, "dueSoonBalance"),
            Dec(summary, "pastDueBalance"),
            Dec(summary, "paidBalance"),
            S(summary, "currency") ?? "USD");
    }

    public async Task<RevenueSummaryRecord> GetRevenueSummaryAsync(long companyId, CancellationToken ct = default)
    {
        await EnsureRevenueLeakageSignalsAsync(companyId, ct);

        var summary = await db.QuerySingleAsync(
            @"SELECT
                COALESCE((SELECT SUM(subtotal) FROM invoice_drafts WHERE company_id=@companyId AND status IN ('draft','pending_review','approved')), 0) AS total_draft_charges,
                COALESCE((SELECT COUNT(*) FROM jobs WHERE company_id=@companyId AND LOWER(status) = 'ready_to_bill'), 0) AS ready_to_bill_jobs_count,
                COALESCE((SELECT COUNT(*) FROM invoice_drafts WHERE company_id=@companyId), 0) AS invoice_drafts_count,
                COALESCE((SELECT SUM(total) FROM invoice_drafts WHERE company_id=@companyId), 0) AS invoice_draft_total,
                COALESCE((SELECT COUNT(*) FROM jobs j WHERE j.company_id=@companyId AND LOWER(j.status) IN ('completed','delivered','ready_to_bill') AND NOT EXISTS (SELECT 1 FROM job_charges jc WHERE jc.company_id=j.company_id AND jc.job_id=j.id)), 0) AS jobs_missing_charges_count,
                COALESCE((SELECT COUNT(*) FROM jobs j WHERE j.company_id=@companyId AND LOWER(j.status) IN ('completed','delivered','ready_to_bill') AND (j.contract_id IS NULL OR j.rate_card_id IS NULL)), 0) AS jobs_missing_contract_or_rate_card_count,
                COALESCE((SELECT COUNT(*) FROM ai_recommendations r WHERE r.tenant_id=@companyId AND r.status IN ('draft','active','approval_required')), 0) AS revenue_leakage_risk_count,
                COALESCE((SELECT currency FROM invoice_drafts WHERE company_id=@companyId ORDER BY created_at DESC LIMIT 1), 'USD') AS currency",
            c => c.Parameters.AddWithValue("@companyId", companyId),
            ct);

        var topCustomers = await db.QueryAsync(
            @"SELECT c.id AS customer_id, c.name AS customer_name, COALESCE(SUM(d.total), 0) AS draft_revenue
              FROM invoice_drafts d
              JOIN customers c ON c.id = d.customer_id
              WHERE d.company_id=@companyId
              GROUP BY c.id, c.name
              ORDER BY draft_revenue DESC, c.name
              LIMIT 5",
            c => c.Parameters.AddWithValue("@companyId", companyId),
            ct);

        return new RevenueSummaryRecord(
            companyId,
            Dec(summary, "totalDraftCharges"),
            L(summary, "readyToBillJobsCount"),
            L(summary, "invoiceDraftsCount"),
            Dec(summary, "invoiceDraftTotal"),
            L(summary, "jobsMissingChargesCount"),
            L(summary, "jobsMissingContractOrRateCardCount"),
            L(summary, "revenueLeakageRiskCount"),
            S(summary, "currency") ?? "USD",
            topCustomers);
    }

    public async Task<CustomerSummaryRecord?> GetCustomerSummaryAsync(long companyId, long customerId, CancellationToken ct = default)
    {
        await EnsureRevenueLeakageSignalsAsync(companyId, ct);

        var customer = await db.QuerySingleAsync(
            @"SELECT id, company_id, customer_code, name, contact_name, email, phone, status, sla_tier, sla_health_score, delivery_experience_score, risk_score
              FROM customers WHERE company_id=@companyId AND id=@customerId AND deleted_at IS NULL LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            },
            ct);
        if (customer is null)
        {
            return null;
        }

        var counts = await db.QuerySingleAsync(
            @"SELECT
                COALESCE((SELECT COUNT(*) FROM contracts con WHERE con.company_id=@companyId AND con.customer_id=@customerId AND con.status='Active'), 0) AS active_contracts_count,
                COALESCE((SELECT COUNT(*) FROM jobs j WHERE j.company_id=@companyId AND j.customer_id=@customerId), 0) AS jobs_count,
                COALESCE((SELECT COUNT(*) FROM jobs j WHERE j.company_id=@companyId AND j.customer_id=@customerId AND LOWER(j.status) IN ('completed','delivered')), 0) AS completed_jobs_count,
                COALESCE((SELECT COUNT(*) FROM jobs j WHERE j.company_id=@companyId AND j.customer_id=@customerId AND LOWER(j.status) = 'ready_to_bill'), 0) AS ready_to_bill_jobs_count,
                COALESCE((SELECT COUNT(*) FROM invoice_drafts d WHERE d.company_id=@companyId AND d.customer_id=@customerId), 0) AS invoice_drafts_count,
                COALESCE((SELECT SUM(total) FROM invoice_drafts d WHERE d.company_id=@companyId AND d.customer_id=@customerId), 0) AS invoice_draft_total,
                COALESCE((SELECT COUNT(*) FROM ai_recommendations r WHERE r.tenant_id=@companyId AND r.status IN ('draft','active','approval_required') AND EXISTS (
                    SELECT 1 FROM jobs j WHERE j.company_id=@companyId AND j.customer_id=@customerId AND r.source_event_id LIKE ('job:' || j.id::text || ':%')
                )), 0) AS open_revenue_leakage_recommendations_count",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            },
            ct);

        var recentJobs = await db.QueryAsync(
            @"SELECT id, job_code, job_type, status, priority, scheduled_start, scheduled_end, created_at
              FROM jobs
              WHERE company_id=@companyId AND customer_id=@customerId
              ORDER BY created_at DESC, id DESC
              LIMIT 5",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            },
            ct);

        var recentCharges = await db.QueryAsync(
            @"SELECT jc.*
              FROM job_charges jc
              JOIN jobs j ON j.id = jc.job_id AND j.company_id = jc.company_id
              WHERE jc.company_id=@companyId AND j.customer_id=@customerId
              ORDER BY jc.created_at DESC, jc.id DESC
              LIMIT 5",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            },
            ct);

        var recentDrafts = await db.QueryAsync(
            @"SELECT *
              FROM invoice_drafts
              WHERE company_id=@companyId AND customer_id=@customerId
              ORDER BY created_at DESC, invoice_draft_no DESC
              LIMIT 5",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@customerId", customerId);
            },
            ct);

        return new CustomerSummaryRecord(
            companyId,
            customerId,
            customer,
            L(counts, "activeContractsCount"),
            L(counts, "jobsCount"),
            L(counts, "completedJobsCount"),
            L(counts, "readyToBillJobsCount"),
            L(counts, "invoiceDraftsCount"),
            Dec(counts, "invoiceDraftTotal"),
            L(counts, "openRevenueLeakageRecommendationsCount"),
            recentJobs,
            recentCharges,
            recentDrafts);
    }

    public async Task<int> EnsureRevenueLeakageSignalsAsync(long companyId, CancellationToken ct = default)
    {
        var jobs = await db.QueryAsync(
            @"SELECT
                j.id,
                j.job_code,
                j.customer_id,
                j.contract_id,
                j.rate_card_id,
                j.status,
                COALESCE((SELECT COUNT(*) FROM job_charges jc WHERE jc.company_id=j.company_id AND jc.job_id=j.id), 0) AS charge_count,
                COALESCE((SELECT COUNT(*) FROM job_charges jc WHERE jc.company_id=j.company_id AND jc.job_id=j.id AND LOWER(jc.status) = 'approved'), 0) AS approved_charge_count,
                COALESCE((SELECT COUNT(*) FROM invoice_drafts d WHERE d.company_id=j.company_id AND d.job_id=j.id AND d.status IN ('draft','pending_review','approved')), 0) AS draft_count
              FROM jobs j
              WHERE j.company_id=@companyId
                AND LOWER(j.status) IN ('completed','delivered','ready_to_bill')",
            c => c.Parameters.AddWithValue("@companyId", companyId),
            ct);

        var created = 0;
        foreach (var job in jobs)
        {
            var jobId = L(job, "id");
            var jobCode = S(job, "jobCode") ?? $"JOB-{jobId}";
            var chargeCount = L(job, "chargeCount");
            var approvedChargeCount = L(job, "approvedChargeCount");
            var draftCount = L(job, "draftCount");
            var source = $"job:{jobId}";

            if (chargeCount == 0)
            {
                if (await EnsureLeakageRecommendationAsync(
                    companyId,
                    jobId,
                    "completed_job_missing_charges",
                    "Completed job missing charges",
                    $"Job {jobCode} is completed but has no charges.",
                    "revenue.leakage_detected",
                    "high",
                    "{\"missing\":\"job_charges\"}",
                    "{\"reason\":\"Completed job has no billable charges\"}",
                    "{\"action\":\"draft_missing_charge_review\"}",
                    $"draft_missing_charge_review:{source}",
                    ct))
                {
                    created++;
                }
            }

            if (job["contractId"] is null || job["rateCardId"] is null)
            {
                if (await EnsureLeakageRecommendationAsync(
                    companyId,
                    jobId,
                    "job_without_contract_or_rate_card",
                    "Job missing contract or rate card",
                    $"Job {jobCode} is missing contract or rate card coverage.",
                    "revenue.leakage_detected",
                    "medium",
                    JsonSerializer.Serialize(new { jobId, jobCode, contractId = job["contractId"], rateCardId = job["rateCardId"] }),
                    "{\"reason\":\"Pricing foundation missing\"}",
                    "{\"action\":\"review_pricing_setup\"}",
                    $"review_pricing_setup:{source}",
                    ct))
                {
                    created++;
                }
            }

            if (string.Equals(S(job, "status"), "ready_to_bill", StringComparison.OrdinalIgnoreCase) && draftCount == 0)
            {
                if (await EnsureLeakageRecommendationAsync(
                    companyId,
                    jobId,
                    "ready_to_bill_job_without_invoice_draft",
                    "Ready-to-bill job without invoice draft",
                    $"Job {jobCode} is ready to bill but no invoice draft exists yet.",
                    "revenue.leakage_detected",
                    "medium",
                    "{\"missing\":\"invoice_draft\"}",
                    "{\"reason\":\"Ready-to-bill job has no draft\"}",
                    "{\"action\":\"create_invoice_draft\"}",
                    $"create_invoice_draft:{source}",
                    ct))
                {
                    created++;
                }
            }

            if (approvedChargeCount > 0 && draftCount == 0)
            {
                if (await EnsureLeakageRecommendationAsync(
                    companyId,
                    jobId,
                    "approved_charges_not_drafted",
                    "Approved charges not drafted into invoice",
                    $"Job {jobCode} has approved charges that are not drafted yet.",
                    "revenue.leakage_detected",
                    "medium",
                    "{\"missing\":\"invoice_draft_lines\"}",
                    "{\"reason\":\"Approved charges are not yet drafted\"}",
                    "{\"action\":\"draft_approved_charge_lines\"}",
                    $"draft_approved_charge_lines:{source}",
                    ct))
                {
                    created++;
                }
            }
        }

        return created;
    }

    private async Task<bool> EnsureLeakageRecommendationAsync(
        long companyId,
        long jobId,
        string recommendationType,
        string title,
        string summary,
        string riskLevel,
        string confidenceReason,
        string impactJson,
        string reasonJson,
        string proposedActionJson,
        string sourceEventId,
        CancellationToken ct)
    {
        var existing = await db.QuerySingleAsync(
            @"SELECT id
              FROM ai_recommendations
              WHERE tenant_id=@tenantId AND recommendation_type=@recommendationType AND source_event_id=@sourceEventId
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@tenantId", companyId);
                c.Parameters.AddWithValue("@recommendationType", recommendationType);
                c.Parameters.AddWithValue("@sourceEventId", sourceEventId);
            },
            ct);
        if (existing is not null)
        {
            return false;
        }

        var recommendation = ai.CreateRecommendation(
            companyId.ToString(CultureInfo.InvariantCulture),
            recommendationType,
            title,
            summary,
            0.90m,
            0.75m,
            impactJson,
            reasonJson,
            proposedActionJson,
            riskLevel,
            sourceEventId,
            ActorTypes.System,
            "revenue-readiness",
            status: "active");

        _ = ai.CreateActionRequest(
            companyId.ToString(CultureInfo.InvariantCulture),
            recommendation.Id,
            JsonDocument.Parse(proposedActionJson).RootElement.TryGetProperty("action", out var action) ? action.GetString() ?? "revenue.review" : "revenue.review",
            "job",
            jobId.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(new { jobId, recommendationType, confidenceReason }),
            riskLevel,
            ActorTypes.System,
            "revenue-readiness",
            requiresApproval: true);

        return true;
    }

    private static bool IsEligibleReadyStatus(string status)
        => string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "delivered", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "ready_to_bill", StringComparison.OrdinalIgnoreCase);

    private static string BuildInvoiceDraftNumber(long companyId, long jobId)
        => $"INV-DRAFT-{companyId}-{jobId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";

    private static JsonDocument ParseMetadata(string? json)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        }
        catch
        {
            return JsonDocument.Parse("{}");
        }
    }

    private async Task<JobSnapshot?> LoadJobAsync(long companyId, long jobId, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT id, company_id, customer_id, contract_id, rate_card_id, job_code, status
              FROM jobs
              WHERE company_id=@companyId AND id=@jobId
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", jobId);
            },
            ct);
        return row is null ? null : new JobSnapshot(L(row, "id"), L(row, "companyId"), LN(row, "customerId"), LN(row, "contractId"), LN(row, "rateCardId"), S(row, "jobCode") ?? string.Empty, S(row, "status") ?? string.Empty);
    }

    private async Task<IReadOnlyList<JobChargeSnapshot>> LoadJobChargesAsync(long companyId, long jobId, CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT id, company_id, job_id, trip_id, rate_card_id, charge_code, charge_name, description, quantity, unit_rate, amount, currency, status
                     , charge_type
              FROM job_charges
              WHERE company_id=@companyId AND job_id=@jobId
              ORDER BY created_at, id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", jobId);
            },
            ct);
        return rows.Select(row => new JobChargeSnapshot(
            L(row, "id"),
            L(row, "companyId"),
            L(row, "jobId"),
            LN(row, "tripId"),
            LN(row, "rateCardId"),
            S(row, "chargeCode") ?? string.Empty,
            S(row, "chargeName") ?? string.Empty,
            S(row, "description"),
            Dec(row, "quantity"),
            Dec(row, "unitRate"),
            Dec(row, "amount"),
            S(row, "currency") ?? "USD",
            S(row, "status") ?? "pending",
            S(row, "chargeType") ?? "base")).ToList();
    }

    private async Task<InvoiceDraftRecord?> LoadActiveInvoiceDraftForJobAsync(long companyId, long jobId, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT *
              FROM invoice_drafts
              WHERE company_id=@companyId AND job_id=@jobId AND status IN ('draft','pending_review','approved')
              ORDER BY created_at DESC
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@jobId", jobId);
            },
            ct);
        return row is null ? null : MapInvoiceDraft(row);
    }

    // Tenant opt-in to POD-gated issuance (feature_flags). Default OFF (no row -> false).
    private async Task<bool> IsPodRequiredToIssueAsync(long companyId, CancellationToken ct)
    {
        try
        {
            var n = await db.ScalarLongAsync(
                @"SELECT CASE WHEN EXISTS (SELECT 1 FROM feature_flags
                    WHERE company_id=@cid AND flag_key='billing.require_pod_to_issue' AND enabled) THEN 1 ELSE 0 END",
                c => c.Parameters.AddWithValue("@cid", companyId), ct);
            return n == 1;
        }
        catch { return false; } // feature_flags absent (pre-migration) -> flag off, unchanged behavior
    }

    // A job "has POD" if EITHER store shows it: proof_of_delivery.status='Captured' (ops/job path)
    // OR dispatch_proofs.proof_type='delivery' (driver/dispatch path). Neither alone is authoritative
    // because the two capture surfaces write different tables; jobs.proof_status is not trustworthy
    // (driver PODs never set it). Both sides are double-scoped by company_id.
    private async Task<bool> HasDeliveryProofAsync(long companyId, long jobId, CancellationToken ct)
    {
        var n = await db.ScalarLongAsync(
            @"SELECT CASE WHEN
                EXISTS (SELECT 1 FROM proof_of_delivery
                        WHERE company_id=@cid AND job_id=@jid AND status='Captured')
                OR EXISTS (SELECT 1 FROM dispatch_proofs dp
                        JOIN dispatch_assignments da ON da.id=dp.assignment_id AND da.company_id=@cid
                        WHERE dp.company_id=@cid AND da.job_id=@jid AND dp.proof_type='delivery')
              THEN 1 ELSE 0 END",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@jid", jobId); }, ct);
        return n == 1;
    }

    private async Task<IssuedInvoiceRecord?> LoadIssuedInvoiceByDraftAsync(long companyId, Guid draftId, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT *
              FROM issued_invoices
              WHERE company_id=@companyId AND source_invoice_draft_id=@draftId
              LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@draftId", draftId);
            },
            ct);

        if (row is null)
        {
            return null;
        }

        var lines = await LoadIssuedInvoiceLinesAsync(companyId, G(row, "id"), ct);
        return MapIssuedInvoice(row, lines);
    }

    private async Task<IssuedInvoiceRecord?> LoadIssuedInvoiceByIdAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long companyId, Guid invoiceId, CancellationToken ct)
    {
        await using var cmd = new Npgsql.NpgsqlCommand(
            @"SELECT *
              FROM issued_invoices
              WHERE company_id=@companyId AND id=@id
              LIMIT 1",
            conn, tx);
        cmd.Parameters.AddWithValue("@companyId", companyId);
        cmd.Parameters.AddWithValue("@id", invoiceId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var row = ReadRow(reader);
        await reader.DisposeAsync();
        var lines = await LoadIssuedInvoiceLinesAsync(conn, tx, companyId, invoiceId, ct);
        return MapIssuedInvoice(row, lines);
    }

    private async Task<string?> GetApprovalRequestStatusAsync(long companyId, long approvalRequestId, CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT status FROM approval_requests WHERE tenant_id=@companyId AND id=@id LIMIT 1",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@id", approvalRequestId);
            },
            ct);
        return row is null ? null : S(row, "status");
    }

    private async Task<IReadOnlyList<IssuedInvoiceLineRecord>> LoadIssuedInvoiceLinesAsync(long companyId, Guid invoiceId, CancellationToken ct)
    {
        var rows = await db.QueryAsync(
            @"SELECT *
              FROM issued_invoice_lines
              WHERE company_id=@companyId AND issued_invoice_id=@invoiceId
              ORDER BY line_no, created_at",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@invoiceId", invoiceId);
            },
            ct);
        return rows.Select(MapIssuedInvoiceLine).ToList();
    }

    private static async Task<IReadOnlyList<IssuedInvoiceLineRecord>> LoadIssuedInvoiceLinesAsync(NpgsqlConnection conn, NpgsqlTransaction tx, long companyId, Guid invoiceId, CancellationToken ct)
    {
        await using var cmd = new Npgsql.NpgsqlCommand(
            @"SELECT *
              FROM issued_invoice_lines
              WHERE company_id=@companyId AND issued_invoice_id=@invoiceId
              ORDER BY line_no, created_at",
            conn, tx);
        cmd.Parameters.AddWithValue("@companyId", companyId);
        cmd.Parameters.AddWithValue("@invoiceId", invoiceId);
        var rows = new List<IssuedInvoiceLineRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(MapIssuedInvoiceLine(ReadRow(reader)));
        }
        return rows;
    }

    private static async Task<InvoicePaymentRecord> InsertInvoicePaymentAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long companyId,
        Guid invoiceId,
        decimal amount,
        string currency,
        string paymentReference,
        string paymentMethod,
        string? metadataJson,
        string? correlationId,
        string? causationId,
        DateTimeOffset receivedAt,
        CancellationToken ct)
    {
        await using var cmd = new Npgsql.NpgsqlCommand(
            @"INSERT INTO invoice_payments
                (company_id, issued_invoice_id, payment_reference, payment_method, currency, amount, received_at, status, metadata_json, correlation_id, causation_id, created_at)
              VALUES
                (@companyId, @invoiceId, @paymentReference, @paymentMethod, @currency, @amount, @receivedAt, 'posted', @metadata::jsonb, @correlationId, @causationId, @createdAt)
              RETURNING id",
            conn, tx);
        cmd.Parameters.AddWithValue("@companyId", companyId);
        cmd.Parameters.AddWithValue("@invoiceId", invoiceId);
        cmd.Parameters.AddWithValue("@paymentReference", paymentReference);
        cmd.Parameters.AddWithValue("@paymentMethod", paymentMethod);
        cmd.Parameters.AddWithValue("@currency", currency);
        cmd.Parameters.AddWithValue("@amount", amount);
        cmd.Parameters.AddWithValue("@receivedAt", receivedAt);
        cmd.Parameters.AddWithValue("@metadata", metadataJson ?? "{}");
        cmd.Parameters.AddWithValue("@correlationId", (object?)correlationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@causationId", (object?)causationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", receivedAt);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        return new InvoicePaymentRecord(id, companyId, invoiceId, paymentReference, paymentMethod, currency, amount, receivedAt, "posted", metadataJson, correlationId, causationId, receivedAt);
    }

    private static InvoiceDraftRecord MapInvoiceDraft(Dictionary<string, object?> row, IReadOnlyList<InvoiceDraftLineRecord>? lines = null)
        => new(
            G(row, "id"),
            L(row, "companyId"),
            L(row, "customerId"),
            LN(row, "contractId"),
            LN(row, "jobId"),
            S(row, "invoiceDraftNo") ?? string.Empty,
            S(row, "status") ?? string.Empty,
            S(row, "currency") ?? "USD",
            Dec(row, "subtotal"),
            Dec(row, "taxTotal"),
            Dec(row, "total"),
            S(row, "source") ?? "system",
            S(row, "metadataJson"),
            Dto(row, "createdAt"),
            DtoN(row, "updatedAt"),
            Guid.TryParse(S(row, "createdBy"), out var createdBy) ? createdBy : null,
            Guid.TryParse(S(row, "updatedBy"), out var updatedBy) ? updatedBy : null,
            LN(row, "approvalRequestId"),
            lines);

    private static InvoiceDraftLineRecord MapInvoiceDraftLine(Dictionary<string, object?> row)
        => new(
            G(row, "id"),
            L(row, "companyId"),
            G(row, "invoiceDraftId"),
            LN(row, "jobChargeId"),
            (int)L(row, "lineNo"),
            S(row, "description") ?? string.Empty,
            S(row, "chargeCode"),
            Dec(row, "quantity"),
            S(row, "unit"),
            Dec(row, "unitRate"),
            Dec(row, "amount"),
            S(row, "metadataJson"),
            Dto(row, "createdAt"),
            DtoN(row, "updatedAt"));

    private static IssuedInvoiceRecord MapIssuedInvoice(Dictionary<string, object?> row, IReadOnlyList<IssuedInvoiceLineRecord>? lines = null)
        => new(
            G(row, "id"),
            L(row, "companyId"),
            G(row, "sourceInvoiceDraftId"),
            L(row, "customerId"),
            LN(row, "contractId"),
            LN(row, "jobId"),
            LN(row, "approvalRequestId"),
            S(row, "invoiceNumber") ?? string.Empty,
            S(row, "status") ?? string.Empty,
            S(row, "currency") ?? "USD",
            Dec(row, "subtotal"),
            Dec(row, "taxTotal"),
            Dec(row, "total"),
            Dec(row, "amountPaid"),
            Dec(row, "balanceDue"),
            S(row, "paymentStatus") ?? "unpaid",
            Dto(row, "issuedAt"),
            DtoN(row, "dueAt"),
            DtoN(row, "paidAt"),
            S(row, "issuedByActorType"),
            S(row, "issuedByActorId"),
            S(row, "correlationId"),
            S(row, "causationId"),
            S(row, "idempotencyKey"),
            S(row, "metadataJson"),
            Dto(row, "createdAt"),
            DtoN(row, "updatedAt"),
            lines);

    private static IssuedInvoiceLineRecord MapIssuedInvoiceLine(Dictionary<string, object?> row)
        => new(
            G(row, "id"),
            L(row, "companyId"),
            G(row, "issuedInvoiceId"),
            Guid.TryParse(S(row, "sourceInvoiceDraftLineId"), out var sourceDraftLineId) ? sourceDraftLineId : null,
            LN(row, "jobChargeId"),
            (int)L(row, "lineNo"),
            S(row, "description") ?? string.Empty,
            S(row, "chargeCode"),
            Dec(row, "quantity"),
            S(row, "unit"),
            Dec(row, "unitRate"),
            Dec(row, "amount"),
            S(row, "metadataJson"),
            Dto(row, "createdAt"),
            DtoN(row, "updatedAt"));

    private static Dictionary<string, object?> ReadRow(Npgsql.NpgsqlDataReader reader)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            row[ToCamel(reader.GetName(i))] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }
        return row;
    }

    private static string ToCamel(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return value;
        }

        return parts[0].ToLowerInvariant() + string.Concat(parts.Skip(1).Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static string BuildIssuedInvoiceNumber(long companyId, Guid draftId)
        => $"INV-{companyId}-{draftId:N}";

    private sealed record JobSnapshot(long Id, long CompanyId, long? CustomerId, long? ContractId, long? RateCardId, string JobCode, string Status);

    private sealed record JobChargeSnapshot(long Id, long CompanyId, long JobId, long? TripId, long? RateCardId, string ChargeCode, string ChargeName, string? Description, decimal Quantity, decimal UnitRate, decimal Amount, string Currency, string Status, string ChargeType);

    private static Guid G(Dictionary<string, object?> row, string key) => Guid.Parse(S(row, key) ?? throw new InvalidOperationException($"Missing GUID column {key}."));
    // ── AR aging: bucket outstanding issued_invoices by days past due_at ───────────
    // Buckets on remaining balance_due (never on total), so a partially-paid invoice
    // ages only its unpaid portion. Company totals + per-customer breakdown.
    public async Task<ArAgingRecord> GetAccountsReceivableAgingAsync(long companyId, CancellationToken ct = default)
    {
        const string bucketSelect = @"
              COALESCE(SUM(CASE WHEN {a}.due_at >  NOW()                       THEN {a}.balance_due ELSE 0 END), 0) AS cur,
              COALESCE(SUM(CASE WHEN {a}.due_at <= NOW()                       AND {a}.due_at > NOW() - INTERVAL '30 days' THEN {a}.balance_due ELSE 0 END), 0) AS b1,
              COALESCE(SUM(CASE WHEN {a}.due_at <= NOW() - INTERVAL '30 days'  AND {a}.due_at > NOW() - INTERVAL '60 days' THEN {a}.balance_due ELSE 0 END), 0) AS b2,
              COALESCE(SUM(CASE WHEN {a}.due_at <= NOW() - INTERVAL '60 days'  AND {a}.due_at > NOW() - INTERVAL '90 days' THEN {a}.balance_due ELSE 0 END), 0) AS b3,
              COALESCE(SUM(CASE WHEN {a}.due_at <= NOW() - INTERVAL '90 days'  THEN {a}.balance_due ELSE 0 END), 0) AS b4,
              COALESCE(SUM({a}.balance_due), 0) AS tot";

        var totals = await db.QuerySingleAsync(
            $@"SELECT {bucketSelect.Replace("{a}", "i")},
                 COALESCE((SELECT currency FROM issued_invoices WHERE company_id=@companyId ORDER BY issued_at DESC LIMIT 1), 'USD') AS currency
               FROM issued_invoices i
               WHERE i.company_id=@companyId AND i.balance_due > 0",
            c => c.Parameters.AddWithValue("@companyId", companyId),
            ct);

        var customerRows = await db.QueryAsync(
            $@"SELECT i.customer_id, COALESCE(c.name, '(unknown)') AS customer_name,
                 {bucketSelect.Replace("{a}", "i")}
               FROM issued_invoices i
               LEFT JOIN customers c ON c.id = i.customer_id AND c.company_id = i.company_id
               WHERE i.company_id=@companyId AND i.balance_due > 0
               GROUP BY i.customer_id, c.name
               ORDER BY tot DESC, i.customer_id",
            c => c.Parameters.AddWithValue("@companyId", companyId),
            ct);

        var customers = customerRows.Select(r => new ArAgingCustomerRecord(
            L(r, "customerId"), S(r, "customerName") ?? "(unknown)",
            Dec(r, "cur"), Dec(r, "b1"), Dec(r, "b2"), Dec(r, "b3"), Dec(r, "b4"),
            Dec(r, "cur") + Dec(r, "b1") + Dec(r, "b2") + Dec(r, "b3") + Dec(r, "b4"))).ToList();

        return new ArAgingRecord(
            companyId, S(totals, "currency") ?? "USD",
            Dec(totals, "cur"), Dec(totals, "b1"), Dec(totals, "b2"), Dec(totals, "b3"), Dec(totals, "b4"),
            Dec(totals, "tot"), customers);
    }

    // ── Payment summary over a date range: collected, outstanding, days-to-pay ─────
    public async Task<PaymentSummaryRecord> GetPaymentSummaryAsync(long companyId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var totals = await db.QuerySingleAsync(
            @"SELECT
                COALESCE((SELECT SUM(amount) FROM invoice_payments WHERE company_id=@companyId AND received_at >= @from AND received_at < @to), 0) AS total_collected,
                COALESCE((SELECT SUM(balance_due) FROM issued_invoices WHERE company_id=@companyId AND balance_due > 0), 0) AS total_outstanding,
                (SELECT COUNT(*) FROM invoice_payments WHERE company_id=@companyId AND received_at >= @from AND received_at < @to) AS payment_count,
                (SELECT COUNT(*) FROM issued_invoices WHERE company_id=@companyId AND payment_status='paid' AND paid_at >= @from AND paid_at < @to) AS paid_invoice_count,
                (SELECT AVG(EXTRACT(EPOCH FROM (paid_at - issued_at)) / 86400.0) FROM issued_invoices WHERE company_id=@companyId AND payment_status='paid' AND paid_at IS NOT NULL AND paid_at >= @from AND paid_at < @to) AS avg_days_to_pay,
                COALESCE((SELECT currency FROM issued_invoices WHERE company_id=@companyId ORDER BY issued_at DESC LIMIT 1), 'USD') AS currency",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@from", from);
                c.Parameters.AddWithValue("@to", to);
            },
            ct);

        var customerRows = await db.QueryAsync(
            @"SELECT c.id AS customer_id, c.name AS customer_name,
                COALESCE((SELECT SUM(p.amount) FROM invoice_payments p JOIN issued_invoices ii ON ii.id = p.issued_invoice_id
                          WHERE ii.company_id=@companyId AND ii.customer_id=c.id AND p.received_at >= @from AND p.received_at < @to), 0) AS total_collected,
                COALESCE((SELECT SUM(ii.balance_due) FROM issued_invoices ii WHERE ii.company_id=@companyId AND ii.customer_id=c.id AND ii.balance_due > 0), 0) AS total_outstanding,
                (SELECT AVG(EXTRACT(EPOCH FROM (ii.paid_at - ii.issued_at)) / 86400.0) FROM issued_invoices ii WHERE ii.company_id=@companyId AND ii.customer_id=c.id AND ii.payment_status='paid' AND ii.paid_at >= @from AND ii.paid_at < @to) AS avg_days_to_pay,
                (SELECT COUNT(*) FROM issued_invoices ii WHERE ii.company_id=@companyId AND ii.customer_id=c.id AND ii.payment_status='paid' AND ii.paid_at >= @from AND ii.paid_at < @to) AS paid_invoice_count
              FROM customers c
              WHERE c.company_id=@companyId
                AND EXISTS (SELECT 1 FROM issued_invoices ii WHERE ii.company_id=@companyId AND ii.customer_id=c.id)
              ORDER BY total_collected DESC, c.id",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@from", from);
                c.Parameters.AddWithValue("@to", to);
            },
            ct);

        var customers = customerRows.Select(r => new PaymentSummaryCustomerRecord(
            L(r, "customerId"), S(r, "customerName") ?? "(unknown)",
            Dec(r, "totalCollected"), Dec(r, "totalOutstanding"), DecN(r, "avgDaysToPay"), L(r, "paidInvoiceCount"))).ToList();

        return new PaymentSummaryRecord(
            companyId, S(totals, "currency") ?? "USD", from, to,
            Dec(totals, "totalCollected"), Dec(totals, "totalOutstanding"), DecN(totals, "avgDaysToPay"),
            L(totals, "paymentCount"), L(totals, "paidInvoiceCount"), customers);
    }

    // ── Revenue leakage detection: persist findings into cost_leakage_items ────────
    // Reuses the existing cost_leakage_items table + /api/cost-leakage/* family
    // (entity_type/entity_id = source ref, category = signal_type, estimated_loss =
    // detected amount, status = open/reviewed/resolved). Idempotent: one open signal
    // per (entity, signal_type).
    public async Task<RevenueLeakageDetectionOutcome> DetectRevenueLeakageAsync(long companyId, int stalenessDays = 7, CancellationToken ct = default)
    {
        var candidates = new List<(string SignalType, string EntityType, long EntityId, decimal Amount, string Severity, string Title, string Description)>();

        // Signal 1 — completed/delivered job with NO charge (uncaptured revenue).
        var noCharge = await db.QueryAsync(
            @"SELECT j.id, j.job_code, COALESCE(rc.minimum_charge, 0) AS expected
              FROM jobs j
              LEFT JOIN rate_cards rc ON rc.id = j.rate_card_id AND rc.company_id = j.company_id
              WHERE j.company_id=@companyId
                AND LOWER(j.status) IN ('completed','delivered','ready_to_bill')
                AND NOT EXISTS (SELECT 1 FROM job_charges jc WHERE jc.company_id=j.company_id AND jc.job_id=j.id)",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);
        foreach (var r in noCharge)
        {
            var amt = Dec(r, "expected");
            candidates.Add(("completed_job_no_charge", "job", L(r, "id"), amt,
                amt >= 500m ? "High" : "Medium",
                $"Completed job {S(r, "jobCode")} has no billable charge",
                "Job is completed/delivered but no job_charge exists — revenue is uncaptured."));
        }

        // Signal 2 — charge stuck in 'draft' past the staleness threshold.
        var stale = await db.QueryAsync(
            @"SELECT jc.id, jc.charge_code, jc.job_id, jc.amount
              FROM job_charges jc
              WHERE jc.company_id=@companyId
                AND LOWER(jc.status) = 'draft'
                AND jc.created_at < NOW() - make_interval(days => @days)",
            c =>
            {
                c.Parameters.AddWithValue("@companyId", companyId);
                c.Parameters.AddWithValue("@days", stalenessDays);
            }, ct);
        foreach (var r in stale)
        {
            var amt = Dec(r, "amount");
            candidates.Add(("stale_draft_charge", "charge", L(r, "id"), amt,
                amt >= 500m ? "High" : "Medium",
                $"Draft charge {S(r, "chargeCode")} uninvoiced for over {stalenessDays} days",
                "Charge has been in draft beyond the staleness threshold — revenue at risk of never being billed."));
        }

        // Signal 3 — completed job billed BELOW the contract minimum charge (has charges but short).
        var below = await db.QueryAsync(
            @"SELECT j.id, j.job_code, rc.minimum_charge, COALESCE(SUM(jc.amount), 0) AS charged
              FROM jobs j
              JOIN rate_cards rc ON rc.id = j.rate_card_id AND rc.company_id = j.company_id
              JOIN job_charges jc ON jc.company_id = j.company_id AND jc.job_id = j.id
              WHERE j.company_id=@companyId
                AND LOWER(j.status) IN ('completed','delivered','ready_to_bill')
                AND rc.minimum_charge > 0
              GROUP BY j.id, j.job_code, rc.minimum_charge
              HAVING COALESCE(SUM(jc.amount), 0) < rc.minimum_charge",
            c => c.Parameters.AddWithValue("@companyId", companyId), ct);
        foreach (var r in below)
        {
            var shortfall = Dec(r, "minimumCharge") - Dec(r, "charged");
            candidates.Add(("below_contract_rate", "job", L(r, "id"), shortfall,
                shortfall >= 200m ? "High" : "Medium",
                $"Job {S(r, "jobCode")} billed below contract minimum",
                "Sum of job charges is below the rate card minimum_charge for this job."));
        }

        var signals = new List<RevenueLeakageSignalRecord>();
        var created = 0;
        var alreadyOpen = 0;
        foreach (var cand in candidates)
        {
            var existingId = await db.ScalarLongAsync(
                @"SELECT COALESCE(MAX(id), 0) FROM cost_leakage_items
                  WHERE company_id=@companyId AND entity_type=@et AND entity_id=@eid AND category=@cat
                    AND status IN ('open','reviewed') AND deleted_at IS NULL",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@et", cand.EntityType);
                    c.Parameters.AddWithValue("@eid", cand.EntityId);
                    c.Parameters.AddWithValue("@cat", cand.SignalType);
                }, ct);

            var leakageNumber = $"RLK-{cand.SignalType}-{cand.EntityId}";
            if (existingId > 0)
            {
                alreadyOpen++;
                signals.Add(new RevenueLeakageSignalRecord(existingId, leakageNumber, cand.SignalType, cand.EntityType, cand.EntityId, cand.Amount, cand.Severity, "open", cand.Title));
                continue;
            }

            var id = await db.InsertAsync(
                @"INSERT INTO cost_leakage_items
                    (company_id, leakage_number, category, entity_type, entity_id, title, description, estimated_loss, severity, status, risk_score, recommended_action, owner_role)
                  VALUES (@companyId, @num, @cat, @et, @eid, @title, @desc, @amount, @sev, 'open', @risk, @action, 'Finance')",
                c =>
                {
                    c.Parameters.AddWithValue("@companyId", companyId);
                    c.Parameters.AddWithValue("@num", leakageNumber);
                    c.Parameters.AddWithValue("@cat", cand.SignalType);
                    c.Parameters.AddWithValue("@et", cand.EntityType);
                    c.Parameters.AddWithValue("@eid", cand.EntityId);
                    c.Parameters.AddWithValue("@title", cand.Title);
                    c.Parameters.AddWithValue("@desc", cand.Description);
                    c.Parameters.AddWithValue("@amount", cand.Amount);
                    c.Parameters.AddWithValue("@sev", cand.Severity);
                    c.Parameters.AddWithValue("@risk", cand.Severity == "High" ? 80m : 50m);
                    c.Parameters.AddWithValue("@action", "Review and bill the uncaptured or underbilled revenue.");
                }, ct);
            created++;
            signals.Add(new RevenueLeakageSignalRecord(id, leakageNumber, cand.SignalType, cand.EntityType, cand.EntityId, cand.Amount, cand.Severity, "open", cand.Title));
        }

        return new RevenueLeakageDetectionOutcome(companyId, created, alreadyOpen, signals);
    }

    private static decimal? DecN(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : null;

    private static string? S(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var value) && value is not null and not DBNull ? value.ToString() : null;
    private static long L(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;
    private static long? LN(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;
    private static decimal Dec(Dictionary<string, object?> row, string key) => row.TryGetValue(key, out var value) && value is not null and not DBNull ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : 0m;
    private static DateTimeOffset Dto(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull
            ? value is DateTimeOffset dto ? dto : new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
    private static DateTimeOffset? DtoN(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var value) && value is not null and not DBNull ? (value is DateTimeOffset dto ? dto : new DateTimeOffset(Convert.ToDateTime(value, CultureInfo.InvariantCulture), TimeSpan.Zero)) : null;
}

// Control-flow signals for the issue transaction's tax gate (ADR-008 P3). Thrown inside the issue
// WithTransactionAsync to roll it back, caught by IssueInvoiceFromDraftAsync to return a typed outcome.
internal sealed class TaxReapprovalException(decimal newTotal) : Exception
{
    public decimal NewTotal { get; } = newTotal;
}

internal sealed class TaxIssueBlockedException(string reason) : Exception
{
    public string Reason { get; } = reason;
}
