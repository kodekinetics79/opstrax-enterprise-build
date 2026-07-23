namespace Opstrax.Telematics.Contracts.Lifecycle;

/// <summary>
/// The authoritative, static allowed-transition map for
/// <see cref="DeviceLifecycleState"/>. This is the single place that decides
/// whether a device may move from one lifecycle state to another; every state
/// machine, worker and admin action MUST validate through <see cref="CanTransition"/>
/// or <see cref="Assert"/> rather than hard-coding its own edges.
/// </summary>
/// <remarks>
/// <para>
/// Design invariants encoded by this map:
/// </para>
/// <list type="bullet">
///   <item><description>There is <b>no direct edge from any provisioning state to
///     <see cref="DeviceLifecycleState.Online"/></b>. Connectivity is only ever
///     reached by passing through <see cref="DeviceLifecycleState.Identified"/> →
///     <see cref="DeviceLifecycleState.Authenticated"/> →
///     <see cref="DeviceLifecycleState.Validating"/>.</description></item>
///   <item><description><see cref="DeviceLifecycleState.Retired"/> is terminal — it
///     has no outgoing edges.</description></item>
///   <item><description>A state is not considered a valid transition to itself; use
///     <see cref="CanTransition"/>'s <c>allowSelf</c> parameter when a no-op refresh
///     should be tolerated.</description></item>
/// </list>
/// </remarks>
public static class LifecycleTransitions
{
    // Adjacency: for each state, the set of states it may legally transition into.
    private static readonly IReadOnlyDictionary<DeviceLifecycleState, IReadOnlySet<DeviceLifecycleState>> Allowed =
        new Dictionary<DeviceLifecycleState, IReadOnlySet<DeviceLifecycleState>>
        {
            [DeviceLifecycleState.Draft] = Set(
                DeviceLifecycleState.Provisioned,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Provisioned] = Set(
                DeviceLifecycleState.AwaitingAssignment,
                DeviceLifecycleState.AwaitingConfiguration,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.AwaitingAssignment] = Set(
                DeviceLifecycleState.AwaitingConfiguration,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.AwaitingConfiguration] = Set(
                DeviceLifecycleState.AwaitingFirstConnection,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.AwaitingFirstConnection] = Set(
                DeviceLifecycleState.Identified,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Identified] = Set(
                DeviceLifecycleState.Authenticated,
                DeviceLifecycleState.AwaitingFirstConnection,
                DeviceLifecycleState.Quarantined,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Authenticated] = Set(
                DeviceLifecycleState.Validating,
                DeviceLifecycleState.Offline,
                DeviceLifecycleState.Quarantined,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Validating] = Set(
                DeviceLifecycleState.Online,
                DeviceLifecycleState.Degraded,
                DeviceLifecycleState.Offline,
                DeviceLifecycleState.Quarantined,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Online] = Set(
                DeviceLifecycleState.Delayed,
                DeviceLifecycleState.Stale,
                DeviceLifecycleState.Degraded,
                DeviceLifecycleState.Offline,
                DeviceLifecycleState.Quarantined,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Delayed] = Set(
                DeviceLifecycleState.Online,
                DeviceLifecycleState.Stale,
                DeviceLifecycleState.Degraded,
                DeviceLifecycleState.Offline,
                DeviceLifecycleState.Quarantined,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Stale] = Set(
                DeviceLifecycleState.Online,
                DeviceLifecycleState.Delayed,
                DeviceLifecycleState.Degraded,
                DeviceLifecycleState.Offline,
                DeviceLifecycleState.Quarantined,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Offline] = Set(
                DeviceLifecycleState.Identified,
                DeviceLifecycleState.Online,
                DeviceLifecycleState.Quarantined,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Degraded] = Set(
                DeviceLifecycleState.Online,
                DeviceLifecycleState.Stale,
                DeviceLifecycleState.Offline,
                DeviceLifecycleState.Quarantined,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Quarantined] = Set(
                DeviceLifecycleState.Validating,
                DeviceLifecycleState.Offline,
                DeviceLifecycleState.Suspended,
                DeviceLifecycleState.Retired),

            [DeviceLifecycleState.Suspended] = Set(
                DeviceLifecycleState.Provisioned,
                DeviceLifecycleState.AwaitingFirstConnection,
                DeviceLifecycleState.Offline,
                DeviceLifecycleState.Retired),

            // Terminal state — no outgoing transitions.
            [DeviceLifecycleState.Retired] = Set(),
        };

    /// <summary>
    /// Returns the set of states that <paramref name="from"/> may legally transition
    /// into. The returned set is read-only and never <see langword="null"/>.
    /// </summary>
    public static IReadOnlySet<DeviceLifecycleState> AllowedFrom(DeviceLifecycleState from) =>
        Allowed.TryGetValue(from, out var set) ? set : Set();

    /// <summary>
    /// Determines whether a device may move from <paramref name="from"/> to
    /// <paramref name="to"/>.
    /// </summary>
    /// <param name="from">The current lifecycle state.</param>
    /// <param name="to">The proposed next lifecycle state.</param>
    /// <param name="allowSelf">
    /// When <see langword="true"/>, a transition where <paramref name="from"/> equals
    /// <paramref name="to"/> is treated as a legal no-op refresh. Defaults to
    /// <see langword="false"/>, so self-transitions are rejected.
    /// </param>
    /// <returns><see langword="true"/> if the transition is permitted.</returns>
    public static bool CanTransition(DeviceLifecycleState from, DeviceLifecycleState to, bool allowSelf = false)
    {
        if (from == to)
            return allowSelf;
        return AllowedFrom(from).Contains(to);
    }

    /// <summary>
    /// Throws <see cref="InvalidLifecycleTransitionException"/> unless the transition
    /// from <paramref name="from"/> to <paramref name="to"/> is permitted. Use this at
    /// the boundary of any state-mutating operation to fail closed on an illegal move.
    /// </summary>
    /// <param name="from">The current lifecycle state.</param>
    /// <param name="to">The proposed next lifecycle state.</param>
    /// <param name="allowSelf">See <see cref="CanTransition"/>.</param>
    /// <exception cref="InvalidLifecycleTransitionException">The transition is not allowed.</exception>
    public static void Assert(DeviceLifecycleState from, DeviceLifecycleState to, bool allowSelf = false)
    {
        if (!CanTransition(from, to, allowSelf))
            throw new InvalidLifecycleTransitionException(from, to);
    }

    private static IReadOnlySet<DeviceLifecycleState> Set(params DeviceLifecycleState[] states) =>
        new HashSet<DeviceLifecycleState>(states);
}

/// <summary>
/// Thrown when an illegal <see cref="DeviceLifecycleState"/> transition is asserted
/// via <see cref="LifecycleTransitions.Assert"/>.
/// </summary>
public sealed class InvalidLifecycleTransitionException : InvalidOperationException
{
    /// <summary>The state the device was in when the illegal transition was attempted.</summary>
    public DeviceLifecycleState From { get; }

    /// <summary>The disallowed target state.</summary>
    public DeviceLifecycleState To { get; }

    /// <summary>Creates the exception for a specific rejected edge.</summary>
    public InvalidLifecycleTransitionException(DeviceLifecycleState from, DeviceLifecycleState to)
        : base($"Illegal device lifecycle transition: {from} -> {to}.")
    {
        From = from;
        To = to;
    }
}
