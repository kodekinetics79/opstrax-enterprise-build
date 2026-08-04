using Opstrax.Api.Foundation;

namespace Opstrax.Api.Services;

// Delivery -> billing automation (ADR-008 §B Phase 2). Consumes the durable job.delivered outbox
// event and turns a delivered load into a billable one: rate it from its contract (idempotent,
// fail-closed if no rate card), then mark it ready-to-bill (idempotent). Both steps are safe to
// re-run, so delivered-fired-twice = one billing action. A failure throws -> the outbox retries
// with backoff and eventually dead-letters; the delivery write already committed upstream and is
// never rolled back (delivery is the source of truth, billing is downstream + retryable).
public sealed class JobDeliveredBillingHandler(RatingService rating, RevenueReadinessService revenue)
    : IOutboxMessageHandler
{
    public string EventType => "job.delivered";

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct = default)
    {
        if (!long.TryParse(message.TenantId, out var companyId)) return;
        if (!long.TryParse(message.AggregateId, out var jobId)) return;

        // Rate first (creates the source='rating' charges); no rate card -> Priced=false, no charges,
        // and MarkReadyToBill will honestly surface the leakage signal rather than bill an empty load.
        await rating.RateJobAsync(companyId, jobId, RateMode.Commit, ct);
        await revenue.MarkJobReadyToBillAsync(companyId, jobId, ct);
    }
}
