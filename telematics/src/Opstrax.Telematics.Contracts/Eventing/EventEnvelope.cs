namespace Opstrax.Telematics.Contracts.Eventing;

/// <summary>
/// The transport-neutral wrapper that carries one payload of type <typeparamref name="T"/>
/// across the durable event backbone. The envelope is the <em>metadata</em> contract that
/// every topic shares regardless of what its payload looks like: identity, causality,
/// timing, ownership scope and schema version live here so that routing, deduplication,
/// tenant isolation and replay never have to crack open the payload to do their job.
/// </summary>
/// <remarks>
/// <para>
/// The envelope is deliberately independent of <see cref="CanonicalTelemetryEvent"/>: the
/// canonical event is <em>one</em> possible <see cref="Payload"/> (the shape carried on the
/// telemetry.* topics), but command, device-health, trip-lifecycle and dead-letter topics
/// carry different payloads under the exact same envelope. Keeping the two apart is what lets
/// a single backbone abstraction serve all 16 topics.
/// </para>
/// <para>
/// <b>Ownership on the envelope mirrors the payload's resolved ownership.</b>
/// <see cref="TenantId"/> and <see cref="CompanyId"/> are copied up from the registry-resolved
/// values so the broker can enforce isolation and build partition keys without deserializing
/// the payload. They are never taken from an untrusted packet — see
/// <see cref="Identity.ResolvedDeviceOwner"/>.
/// </para>
/// <para>
/// The record is immutable and built with object-initializer syntax. Required identity,
/// timing and ownership fields have no defaults on purpose so a half-populated envelope fails
/// to compile rather than silently publishing an event with a zeroed correlation id or scope.
/// </para>
/// </remarks>
/// <typeparam name="T">
/// The payload carried on the topic (for example <see cref="CanonicalTelemetryEvent"/> on the
/// telemetry.* topics, or a command/health/lifecycle payload on the control topics).
/// </typeparam>
public sealed record EventEnvelope<T>
{
    /// <summary>
    /// Globally unique id for this specific envelope. This is the broker-level idempotency
    /// key: a consumer that has already processed a given <see cref="EventId"/> may safely
    /// drop a redelivery of it (at-least-once delivery is assumed).
    /// </summary>
    public required Guid EventId { get; init; }

    /// <summary>
    /// Correlates every envelope and side-effect derived from the same originating frame,
    /// ingest request or operator action, so a single fix (or command) can be traced
    /// end-to-end across every topic hop in the pipeline.
    /// </summary>
    public required Guid CorrelationId { get; init; }

    /// <summary>
    /// Causality pointer to the <see cref="EventId"/> of the envelope that directly produced
    /// this one, when this event is a derivation of an earlier one (for example a
    /// <c>telemetry.normalized</c> event points back at the <c>telemetry.decoded</c> event it
    /// was derived from). <see langword="null"/> for a root event that entered the fabric fresh.
    /// </summary>
    public Guid? CausationId { get; init; }

    /// <summary>
    /// When the fact this envelope reports actually occurred (UTC-anchored). For a derived
    /// event this is the originating observation/action time, not the time the derivation ran,
    /// so ordering and windowing stay stable across pipeline stages.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Owning tenant, copied up from the registry-resolved ownership of the payload. This is
    /// the authoritative isolation scope the broker uses for the tenant segment of the
    /// partition key and for cross-tenant leak prevention.
    /// </summary>
    public required Guid TenantId { get; init; }

    /// <summary>Owning company within the tenant, copied up from the payload's resolved ownership.</summary>
    public required long CompanyId { get; init; }

    /// <summary>
    /// The schema version the <see cref="Payload"/> was produced against. Consumers gate on
    /// this to stay forward/backward compatible; for telemetry payloads it mirrors
    /// <see cref="CanonicalTelemetryEvent.SchemaVersion"/>.
    /// </summary>
    public required int SchemaVersion { get; init; }

    /// <summary>The domain payload this envelope carries. Never <see langword="null"/>.</summary>
    public required T Payload { get; init; }

    /// <summary>
    /// Free-form, string-keyed transport metadata (trace headers, source host, adapter name,
    /// content-type, …) that rides alongside the payload without being part of it. Defaults to
    /// empty. Do not put ownership or causality facts here — those are first-class fields above.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Convenience factory for a root envelope: mints a fresh <see cref="EventId"/>, defaults
    /// <see cref="OccurredAt"/> to now when not supplied, and leaves
    /// <see cref="CausationId"/> null. Prefer this at the edge where events first enter the
    /// fabric; use <c>with</c>-expressions to derive downstream envelopes.
    /// </summary>
    /// <param name="tenantId">Resolved owning tenant.</param>
    /// <param name="companyId">Resolved owning company.</param>
    /// <param name="payload">The domain payload.</param>
    /// <param name="correlationId">
    /// Correlation id to thread through; when <see langword="null"/> a new one is minted and
    /// this envelope becomes the root of a new correlation chain.
    /// </param>
    /// <param name="schemaVersion">Payload schema version. Defaults to 1.</param>
    /// <param name="occurredAt">When the fact occurred; defaults to <see cref="DateTimeOffset.UtcNow"/>.</param>
    public static EventEnvelope<T> Create(
        Guid tenantId,
        long companyId,
        T payload,
        Guid? correlationId = null,
        int schemaVersion = 1,
        DateTimeOffset? occurredAt = null) =>
        new()
        {
            EventId = Guid.NewGuid(),
            CorrelationId = correlationId ?? Guid.NewGuid(),
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
            TenantId = tenantId,
            CompanyId = companyId,
            SchemaVersion = schemaVersion,
            Payload = payload,
        };
}
