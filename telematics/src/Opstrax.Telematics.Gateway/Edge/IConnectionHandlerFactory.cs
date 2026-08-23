using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Forwarding;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Security.Auth;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.Gateway.Edge;

/// <summary>
/// Builds the per-connection handler for an accepted socket. This is the seam that lets one
/// accept loop — with its quota, shed and drain behaviour — serve both egress topologies.
/// </summary>
/// <remarks>
/// The two handlers are separate types rather than one type with a mode flag, on purpose. The
/// canonical path resolves ownership from a registry and writes the database directly; the
/// forwarding path deliberately cannot do either. Keeping them apart means the forwarding edge
/// has no code path that could reach a tenant lookup or a Postgres connection even if
/// misconfigured, and it leaves the tested canonical path untouched.
/// </remarks>
internal interface IConnectionHandlerFactory
{
    /// <summary>Low-cardinality label describing what this factory serves, for logs and metric tags.</summary>
    string Describe();

    /// <summary>Serves one accepted connection to completion. Must never throw; it owns disposing the client.</summary>
    Task HandleAsync(TcpClient client, CancellationToken stoppingToken);
}

/// <summary>
/// Builds the original <see cref="GatewayConnection"/>: registry-resolved ownership, canonical
/// events, direct Postgres projection. Used when <see cref="EgressMode.Postgres"/> is configured.
/// </summary>
internal sealed class CanonicalConnectionHandlerFactory : IConnectionHandlerFactory
{
    private readonly IEventBackbone _backbone;
    private readonly IDeviceRegistry _registry;
    private readonly IDeviceAuthenticator _authenticator;
    private readonly ITelemetryReplayGuard _replayGuard;
    private readonly IPositionProjectionStore _projectionStore;
    private readonly Gt06Adapter _adapter;
    private readonly IStoreAndForwardBuffer _forwardBuffer;
    private readonly GatewayOptions _options;
    private readonly GatewayMetrics _metrics;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates the factory from the canonical pipeline's dependencies.</summary>
    public CanonicalConnectionHandlerFactory(
        IEventBackbone backbone,
        IDeviceRegistry registry,
        IDeviceAuthenticator authenticator,
        ITelemetryReplayGuard replayGuard,
        IPositionProjectionStore projectionStore,
        Gt06Adapter adapter,
        IStoreAndForwardBuffer forwardBuffer,
        GatewayOptions options,
        GatewayMetrics metrics,
        ILoggerFactory loggerFactory)
    {
        _backbone = backbone ?? throw new ArgumentNullException(nameof(backbone));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _replayGuard = replayGuard ?? throw new ArgumentNullException(nameof(replayGuard));
        _projectionStore = projectionStore ?? throw new ArgumentNullException(nameof(projectionStore));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _forwardBuffer = forwardBuffer ?? throw new ArgumentNullException(nameof(forwardBuffer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    public string Describe() => $"{Gt06Adapter.ProtocolName} -> canonical pipeline (direct Postgres projection)";

    /// <inheritdoc />
    public Task HandleAsync(TcpClient client, CancellationToken stoppingToken)
    {
        var connection = new GatewayConnection(
            client,
            _adapter,
            _registry,
            _authenticator,
            _replayGuard,
            _projectionStore,
            _backbone,
            _forwardBuffer,
            _options,
            _metrics,
            _loggerFactory.CreateLogger<GatewayConnection>());

        return connection.RunAsync(stoppingToken);
    }
}

/// <summary>
/// Builds the <see cref="ForwardingConnection"/>: IMEI allowlist, replay defence, HTTPS forwarding
/// to OpsTrax, durable outbox. Used when <see cref="EgressMode.Https"/> is configured — the mode a
/// public, internet-facing VPS edge runs in.
/// </summary>
internal sealed class ForwardingConnectionHandlerFactory : IConnectionHandlerFactory
{
    private readonly ProtocolRouter _router;
    private readonly ImeiAllowlist _allowlist;
    private readonly ITelemetryReplayGuard _replayGuard;
    private readonly IOpstraxForwarder _forwarder;
    private readonly IForwardOutbox _outbox;
    private readonly GatewayOptions _options;
    private readonly string? _edgeInstance;
    private readonly GatewayMetrics _metrics;
    private readonly EdgeMetrics _edgeMetrics;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Creates the factory from the forwarding edge's dependencies.</summary>
    public ForwardingConnectionHandlerFactory(
        ProtocolRouter router,
        ImeiAllowlist allowlist,
        ITelemetryReplayGuard replayGuard,
        IOpstraxForwarder forwarder,
        IForwardOutbox outbox,
        GatewayOptions options,
        EdgeOptions edgeOptions,
        GatewayMetrics metrics,
        EdgeMetrics edgeMetrics,
        ILoggerFactory loggerFactory)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _allowlist = allowlist ?? throw new ArgumentNullException(nameof(allowlist));
        _replayGuard = replayGuard ?? throw new ArgumentNullException(nameof(replayGuard));
        _forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _edgeMetrics = edgeMetrics ?? throw new ArgumentNullException(nameof(edgeMetrics));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        ArgumentNullException.ThrowIfNull(edgeOptions);
        _edgeInstance = string.IsNullOrWhiteSpace(edgeOptions.Forward.EdgeInstance)
            ? Environment.MachineName
            : edgeOptions.Forward.EdgeInstance;
    }

    /// <inheritdoc />
    public string Describe() => $"{_router.Describe()} -> HTTPS forward to OpsTrax";

    /// <inheritdoc />
    public Task HandleAsync(TcpClient client, CancellationToken stoppingToken)
    {
        var connection = new ForwardingConnection(
            client,
            _router,
            _allowlist,
            _replayGuard,
            _forwarder,
            _outbox,
            _options,
            _edgeInstance,
            _metrics,
            _edgeMetrics,
            _loggerFactory.CreateLogger<ForwardingConnection>());

        return connection.RunAsync(stoppingToken);
    }
}
