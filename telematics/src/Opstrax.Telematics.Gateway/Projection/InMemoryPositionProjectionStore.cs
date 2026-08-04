using Opstrax.Telematics.Contracts;

namespace Opstrax.Telematics.Gateway.Projection;

/// <summary>
/// In-process implementation of <see cref="IPositionProjectionStore"/> for tests and dev.
/// Models exactly the two DB-level invariants of the Postgres store without a database:
/// a <see cref="HashSet{T}"/> of seen <see cref="CanonicalTelemetryEvent.EventId"/>s (the inbox
/// dedupe) and a per-vehicle last-write-wins-by-fix-time snapshot (the monotonic upsert).
/// </summary>
/// <remarks>
/// All state is guarded by a single lock: <see cref="ApplyAsync"/> may be called concurrently
/// from multiple projector workers, and the seen-set check and the snapshot compare-and-set must
/// be one atomic step or a duplicate/stale event could slip through the gap between them.
/// </remarks>
internal sealed class InMemoryPositionProjectionStore : IPositionProjectionStore
{
    private readonly object _gate = new();

    /// <summary>Every <see cref="CanonicalTelemetryEvent.EventId"/> ever applied — the idempotency ledger.</summary>
    private readonly HashSet<Guid> _seen = new();

    /// <summary>Current best fix per (company, vehicle): the live-map snapshot.</summary>
    private readonly Dictionary<(long CompanyId, long VehicleId), CanonicalTelemetryEvent> _latest = new();

    /// <inheritdoc />
    public Task<ProjectionOutcome> ApplyAsync(CanonicalTelemetryEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Apply(evt));
    }

    /// <summary>Synchronous core, exposed for deterministic tests.</summary>
    internal ProjectionOutcome Apply(CanonicalTelemetryEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        lock (_gate)
        {
            // Idempotency first: a redelivery of an already-seen event is a hard no-op, whatever
            // it carries. Adding returns false when the id was already present.
            if (!_seen.Add(evt.EventId))
                return ProjectionOutcome.DuplicateIgnored;

            // The event is now recorded as "seen" (so any later redelivery of THIS id, even a
            // stale or vehicle-less one, is deduped), but it only reaches the snapshot when it
            // actually carries a position bound to a vehicle.
            if (evt.Location is null)
                return ProjectionOutcome.NoLocation;

            if (evt.VehicleId is not { } vehicleId)
                return ProjectionOutcome.NoVehicle;

            var key = (evt.CompanyId, vehicleId);

            // Monotonic in device fix time: never let an older fix overwrite a newer stored one.
            // Equal timestamps are allowed to apply (last-write-wins); true duplicates were already
            // filtered by the seen-set above, so an equal-time apply is a distinct, legitimate fix.
            if (_latest.TryGetValue(key, out CanonicalTelemetryEvent? current)
                && evt.OccurredAtDeviceUtc < current.OccurredAtDeviceUtc)
            {
                return ProjectionOutcome.StaleIgnored;
            }

            _latest[key] = evt;
            return ProjectionOutcome.Applied;
        }
    }

    /// <summary>Number of distinct <see cref="CanonicalTelemetryEvent.EventId"/>s applied. Test observability.</summary>
    internal int SeenCount
    {
        get { lock (_gate) return _seen.Count; }
    }

    /// <summary>
    /// The current snapshot fix for a vehicle, or <see langword="null"/> if none has projected.
    /// Test observability into the last-write-wins state.
    /// </summary>
    internal CanonicalTelemetryEvent? Latest(long companyId, long vehicleId)
    {
        lock (_gate)
            return _latest.TryGetValue((companyId, vehicleId), out CanonicalTelemetryEvent? evt) ? evt : null;
    }
}
