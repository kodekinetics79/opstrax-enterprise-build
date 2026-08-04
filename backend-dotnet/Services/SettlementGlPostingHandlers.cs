using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

// AP -> GL derive-beside handlers (blueprint gl-post-ap-settlement). The settlement subledger stays the
// book of record; these post the GL consequence off the durable outbox, idempotently:
//   settlement.approved -> accrue the payable   (Dr 5000 Driver Pay Expense / Cr 2000 Accounts Payable)
//   settlement.paid     -> relieve the payable  (Dr 2000 Accounts Payable  / Cr 1000 Cash)
// A throw -> the outbox retries/dead-letters; the approval/payment write already committed and is never
// rolled back. The dispatcher drains under the platform-admin bypass scope, so the GL methods' explicit
// WHERE company_id=@c is the tenant boundary; companyId is derived only from the event's TenantId.

public sealed class SettlementApprovedGlPostingHandler(GeneralLedgerService gl) : IOutboxMessageHandler
{
    public string EventType => "settlement.approved";

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
    {
        if (!long.TryParse(message.TenantId, out var companyId)) return;
        if (!long.TryParse(message.AggregateId, out var statementId)) return;
        await gl.PostSettlementAsync(companyId, statementId, ct);
    }
}

public sealed class SettlementPaymentGlPostingHandler(GeneralLedgerService gl) : IOutboxMessageHandler
{
    public string EventType => "settlement.paid";

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
    {
        if (!long.TryParse(message.TenantId, out var companyId)) return;
        if (!long.TryParse(message.AggregateId, out var paymentId)) return;
        await gl.PostSettlementPaymentAsync(companyId, paymentId, ct);
    }
}
