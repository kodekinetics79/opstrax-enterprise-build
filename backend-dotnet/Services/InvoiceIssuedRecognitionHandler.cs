using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

// Rev-rec (ADR-008) derive-beside handler. Consumes the durable invoice.issued outbox event and
// recognizes revenue for the invoice (idempotent, fail-closed with no revrec profile). Derive-beside:
// it only writes the recognition sub-ledger, never touches issued_invoices. A failure throws -> the
// outbox retries/dead-letters; issuance already committed and is never rolled back.
public sealed class InvoiceIssuedRecognitionHandler(RevenueRecognitionService revrec) : IOutboxMessageHandler
{
    public string EventType => "invoice.issued";

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
    {
        if (!long.TryParse(message.TenantId, out var companyId)) return;
        if (!Guid.TryParse(message.AggregateId, out var invoiceId)) return;
        await revrec.RecognizeInvoiceAsync(companyId, invoiceId, RecognitionMode.Commit, ct);
    }
}
