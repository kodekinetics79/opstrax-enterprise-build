namespace Opstrax.Telematics.Gateway;

/// <summary>
/// Deployment-time knobs for the TCP device edge gateway, bound from the <c>Gateway</c>
/// section of <c>appsettings.json</c> (or any other configuration provider).
/// </summary>
/// <remarks>
/// <para>
/// Every value here is a <b>safety bound</b>, not a tuning nicety: a device edge listens on
/// the open internet and its peers are unauthenticated, attacker-controlled sockets until
/// the registry says otherwise. The quotas below are what keep one hostile or wedged peer
/// from consuming the host's memory, sockets or CPU.
/// </para>
/// <para>
/// <b>No secrets belong in this type.</b> Device credentials are never carried on the
/// configuration surface — the registry hands back an opaque
/// <c>ResolvedDeviceOwner.CredentialHandle</c> instead.
/// </para>
/// </remarks>
public sealed class GatewayOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string SectionName = "Gateway";

    /// <summary>
    /// IP address the listener binds to. Defaults to <c>127.0.0.1</c> (loopback) so dev and
    /// the integration tests are private and safe-by-default and NOTHING is exposed unless a
    /// deployment opts in. To run the gateway as a reachable device edge, set this to
    /// <c>0.0.0.0</c> (all IPv4 interfaces), <c>::</c> (all IPv6), or a specific interface
    /// address. Parsed via <see cref="ResolveListenAddress"/>; an unparseable value fails
    /// closed to loopback rather than silently binding a wider interface.
    /// </summary>
    public string ListenAddress { get; set; } = "127.0.0.1";

    /// <summary>
    /// TCP port to listen on. <c>0</c> asks the OS for an ephemeral port, which is what the
    /// integration tests use so they never collide with a developer's running gateway.
    /// </summary>
    public int ListenPort { get; set; } = 5023;

    /// <summary>
    /// Parses <see cref="ListenAddress"/> into an <see cref="System.Net.IPAddress"/>. An
    /// empty, whitespace, or unparseable value resolves to <see cref="System.Net.IPAddress.Loopback"/>
    /// — binding wider than configured is a security regression, so a bad value must never
    /// widen exposure. The empty string maps to loopback for the same reason.
    /// </summary>
    public System.Net.IPAddress ResolveListenAddress()
        => System.Net.IPAddress.TryParse((ListenAddress ?? string.Empty).Trim(), out var addr)
            ? addr
            : System.Net.IPAddress.Loopback;

    /// <summary>
    /// Hard ceiling on concurrently accepted connections. Once reached, further connections
    /// are accepted-then-immediately-closed and counted, rather than queued — shedding load
    /// deterministically beats collapsing under it.
    /// </summary>
    public int MaxConnections { get; set; } = 1024;

    /// <summary>
    /// Per-connection cap on canonical events awaiting publication to the backbone. The
    /// publish queue is bounded: when it fills, the connection's read loop stops reading
    /// from the socket, which propagates backpressure all the way to the device's TCP
    /// window instead of growing an unbounded in-memory queue.
    /// </summary>
    public int MaxInFlightPerConnection { get; set; } = 64;

    /// <summary>
    /// Hard ceiling on the bytes a single connection may accumulate without yielding a
    /// complete frame. Bounds the reassembly buffer so a peer cannot force unbounded growth
    /// by dribbling a frame that never terminates.
    /// </summary>
    public int MaxFrameBytes { get; set; } = 2048;

    /// <summary>
    /// How long a connection may stay silent before the gateway closes it. Trackers on
    /// cellular links drop off without a FIN constantly; without this, dead sockets
    /// accumulate until the connection quota is exhausted.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long <see cref="TcpGatewayService.StopAsync"/> waits for in-flight connections to
    /// finish publishing their queued events before abandoning them. This is what makes
    /// shutdown a drain rather than a data-loss event.
    /// </summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Size of the per-connection socket read buffer.</summary>
    public int ReadBufferBytes { get; set; } = 4096;
}
