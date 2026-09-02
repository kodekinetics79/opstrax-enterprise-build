namespace Opstrax.Telematics.Gateway.Security;

/// <summary>
/// Tracks which TCP session is currently authoritative for each device, so a fleet where a
/// tracker reconnects before its previous socket has died converges on exactly one live session
/// per device instead of two sockets both believing they own it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> A cellular tracker loses its bearer without sending a FIN constantly:
/// the tower drops, the device redials, and the gateway now holds a half-open socket for the same
/// IMEI alongside a live one. Until one of them times out, both are bound to the same device, both
/// publish under it, and the order the fleet sees is whichever socket happens to write first.
/// </para>
/// <para>
/// <b>Policy: latest admitted login wins.</b> The newest successfully authenticated session becomes
/// authoritative and the one it displaced is cancelled. This is the right way round for a roaming
/// tracker: the new socket is the one that can actually reach the device, and refusing it would
/// strand the vehicle behind a corpse connection until the idle timeout expires.
/// </para>
/// <para>
/// <b>Leases, not keys.</b> The dangerous version of this class is one where a departing connection
/// removes its device key on the way out. That is an ABA race: A is displaced by B, A's finally
/// block then runs and deletes the entry B just installed, and B is silently no longer registered —
/// so a third connection C displaces nobody and the fleet is back to two authoritative sockets. So
/// every acquisition mints a unique, monotonically increasing lease id, and
/// <see cref="Release(string, long)"/> removes the entry <b>only</b> if the stored lease id is
/// still the caller's. A late release from a displaced session is a no-op by construction.
/// </para>
/// <para>
/// <b>Scope.</b> Per gateway process. Two gateway instances behind a load balancer can each hold a
/// session for the same device; that is a cross-instance problem which needs a shared store, and
/// nothing in the current single-instance edge topology requires one. The durable replay ledger,
/// not this registry, is what keeps two instances from double-counting a frame.
/// </para>
/// <para><b>Thread-safety.</b> All operations are safe for concurrent use from any connection task.</para>
/// </remarks>
internal sealed class ActiveDeviceSessionRegistry
{
    /// <summary>
    /// Guards <see cref="_sessions"/>. Acquire/Release are once-per-login operations, not hot-path
    /// ones, so a single mutex is both sufficient and far easier to prove correct than a lock-free
    /// swap that has to return the displaced value atomically.
    /// </summary>
    private readonly object _gate = new();

    private readonly Dictionary<string, Entry> _sessions = new(StringComparer.Ordinal);

    /// <summary>Source of lease ids. Monotonic and process-wide, so a lease id is never reused.</summary>
    private long _leaseSequence;

    /// <summary>Devices currently holding a session. Gauge, for tests and health output.</summary>
    public int ActiveSessionCount
    {
        get { lock (_gate) return _sessions.Count; }
    }

    /// <summary>
    /// Makes <paramref name="closeSignal"/> the authoritative session for <paramref name="deviceKey"/>,
    /// displacing whatever held it before.
    /// </summary>
    /// <param name="deviceKey">
    /// The canonical device identity to partition on — the registry-resolved device id on the
    /// canonical path, the admitted IMEI on the forwarding edge, which is the only stable identity
    /// that path has. Never a tenant or company.
    /// </param>
    /// <param name="closeSignal">
    /// The new session's cancellation source. It is stored, not triggered; a later acquisition for
    /// the same device is what cancels it.
    /// </param>
    /// <returns>
    /// The acquisition result: the caller's own lease, and the displaced session's cancellation
    /// source when one was evicted.
    /// </returns>
    public SessionAcquisition Acquire(string deviceKey, CancellationTokenSource closeSignal)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceKey);
        ArgumentNullException.ThrowIfNull(closeSignal);

        long leaseId = Interlocked.Increment(ref _leaseSequence);
        CancellationTokenSource? displaced = null;

        lock (_gate)
        {
            if (_sessions.TryGetValue(deviceKey, out Entry previous))
                displaced = previous.CloseSignal;

            _sessions[deviceKey] = new Entry(leaseId, closeSignal);
        }

        // Deliberately OUTSIDE the lock: cancelling runs the displaced connection's continuations,
        // which may re-enter this registry to release their own lease. Cancelling under _gate would
        // deadlock the moment that happens on the same thread.
        return new SessionAcquisition(leaseId, displaced);
    }

    /// <summary>
    /// Releases <paramref name="deviceKey"/> if and only if <paramref name="leaseId"/> is still the
    /// current lease. A displaced session calling this after being replaced changes nothing.
    /// </summary>
    /// <param name="deviceKey">The device key the caller registered under.</param>
    /// <param name="leaseId">The lease id returned by the caller's own <see cref="Acquire"/>.</param>
    /// <returns><see langword="true"/> when this call actually removed the entry.</returns>
    public bool Release(string deviceKey, long leaseId)
    {
        if (string.IsNullOrEmpty(deviceKey)) return false;

        lock (_gate)
        {
            if (!_sessions.TryGetValue(deviceKey, out Entry current) || current.LeaseId != leaseId)
                return false; // Someone newer owns it (or nobody does). Not ours to remove.

            _sessions.Remove(deviceKey);
            return true;
        }
    }

    /// <summary>Whether <paramref name="leaseId"/> is still the authoritative lease for the device.</summary>
    public bool IsCurrent(string deviceKey, long leaseId)
    {
        if (string.IsNullOrEmpty(deviceKey)) return false;

        lock (_gate)
            return _sessions.TryGetValue(deviceKey, out Entry current) && current.LeaseId == leaseId;
    }

    private readonly record struct Entry(long LeaseId, CancellationTokenSource CloseSignal);
}

/// <summary>The outcome of claiming a device session.</summary>
/// <param name="LeaseId">
/// The caller's lease. It must be passed back to
/// <see cref="ActiveDeviceSessionRegistry.Release(string, long)"/> so a stale release cannot evict
/// a newer session.
/// </param>
/// <param name="DisplacedSession">
/// The cancellation source of the session this acquisition evicted, or <see langword="null"/> when
/// the device had no live session. The caller cancels it to tear that connection down.
/// </param>
internal readonly record struct SessionAcquisition(long LeaseId, CancellationTokenSource? DisplacedSession)
{
    /// <summary>Whether this acquisition evicted a previously authoritative session.</summary>
    public bool DisplacedAnother => DisplacedSession is not null;
}
