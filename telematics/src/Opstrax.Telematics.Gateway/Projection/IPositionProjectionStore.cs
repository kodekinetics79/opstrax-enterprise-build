using Opstrax.Telematics.Contracts;

namespace Opstrax.Telematics.Gateway.Projection;

/// <summary>
/// Folds canonical telemetry events down into the live-map snapshot
/// (<c>latest_vehicle_positions</c>), turning an at-least-once, possibly-reordered
/// stream into a single correct "where is this vehicle now" row per vehicle.
/// </summary>
/// <remarks>
/// <para>
/// Two independent hazards make a naive projector wrong, and this seam exists to defeat both:
/// </para>
/// <list type="number">
///   <item><description>
///     <b>Duplication.</b> The backbone delivers at-least-once and the gateway's store-and-forward
///     replay re-emits parked events after an outage, so the same
///     <see cref="CanonicalTelemetryEvent.EventId"/> WILL arrive more than once.
///     <see cref="ApplyAsync"/> is <b>idempotent</b>: a second application of an already-seen
///     <see cref="CanonicalTelemetryEvent.EventId"/> is a no-op (<see cref="ProjectionOutcome.DuplicateIgnored"/>),
///     it must not double-count or re-stamp anything.
///   </description></item>
///   <item><description>
///     <b>Reordering.</b> A delayed replay can land <em>after</em> a fresher live fix for the same
///     vehicle. <see cref="ApplyAsync"/> is <b>monotonic in fix time</b>: it never overwrites a
///     stored fix with one bearing an older <see cref="CanonicalTelemetryEvent.OccurredAtDeviceUtc"/>
///     (<see cref="ProjectionOutcome.StaleIgnored"/>). Last-write-wins is decided by the device fix
///     clock, not by arrival order.
///   </description></item>
/// </list>
/// <para>
/// The two guarantees compose: store-and-forward preserves per-device order on the happy path,
/// and this store stays correct even when that order is lost — so a downstream outage can neither
/// lose a fix nor corrupt the snapshot with a stale one.
/// </para>
/// </remarks>
internal interface IPositionProjectionStore
{
    /// <summary>
    /// Applies one canonical event to the live-position snapshot, idempotently and monotonically.
    /// Safe to call repeatedly with the same event and safe to call with events out of fix-time
    /// order; the returned <see cref="ProjectionOutcome"/> reports which rule (if any) suppressed
    /// the write.
    /// </summary>
    /// <param name="evt">The canonical event to project. Its registry-resolved ownership is authoritative.</param>
    /// <param name="cancellationToken">Cancels the projection.</param>
    Task<ProjectionOutcome> ApplyAsync(CanonicalTelemetryEvent evt, CancellationToken cancellationToken = default);
}

/// <summary>
/// The disposition of a single <see cref="IPositionProjectionStore.ApplyAsync"/> call. Every value
/// except <see cref="Applied"/> is a deliberate, correct no-op — not an error.
/// </summary>
internal enum ProjectionOutcome
{
    /// <summary>The snapshot was updated with this fix (fresh insert or a not-older update).</summary>
    Applied = 0,

    /// <summary>This <see cref="CanonicalTelemetryEvent.EventId"/> had already been projected; nothing was written (idempotency hit).</summary>
    DuplicateIgnored = 1,

    /// <summary>A newer or equal fix for this vehicle is already stored; the older fix was not applied (monotonicity guard).</summary>
    StaleIgnored = 2,

    /// <summary>The event carried no geographic fix (heartbeat/status), so there is no position to project. Still deduped.</summary>
    NoLocation = 3,

    /// <summary>The event was not bound to a vehicle, so it cannot project onto the per-vehicle snapshot. Still deduped.</summary>
    NoVehicle = 4,
}
