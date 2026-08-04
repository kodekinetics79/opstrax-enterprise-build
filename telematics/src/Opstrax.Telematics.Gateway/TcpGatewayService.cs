using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Observability;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Security.Auth;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.Gateway;

/// <summary>
/// The device edge: a <see cref="TcpListener"/>-backed <see cref="BackgroundService"/> that
/// accepts GT06 tracker connections and hands each one to an isolated
/// <see cref="GatewayConnection"/>.
/// </summary>
/// <remarks>
/// <para>
/// The accept loop's only jobs are to enforce the connection quota and to spawn connections
/// that can never take it down. Every per-connection failure is handled inside
/// <see cref="GatewayConnection.RunAsync"/>, so this loop has no path by which one bad peer
/// stops it accepting the next one.
/// </para>
/// <para>
/// <b>Binding happens in <see cref="StartAsync"/>, not <see cref="ExecuteAsync"/>.</b> That is
/// what makes a bind failure (port in use) a startup failure the host reports, rather than a
/// silently-faulted background task — and it is what lets callers read <see cref="BoundPort"/>
/// the moment start returns, which is how the integration tests run on an ephemeral port
/// without racing the listener.
/// </para>
/// </remarks>
internal sealed class TcpGatewayService : BackgroundService
{
    /// <summary>The OTel gateway label for the active-connections gauge. Bounded, low-cardinality.</summary>
    private static readonly string GatewayInstance = Environment.MachineName;

    private readonly GatewayOptions _options;
    private readonly IEventBackbone _backbone;
    private readonly IDeviceRegistry _registry;
    private readonly IDeviceAuthenticator _authenticator;
    private readonly ITelemetryReplayGuard _replayGuard;
    private readonly IPositionProjectionStore _projectionStore;
    private readonly Gt06Adapter _adapter;
    private readonly IStoreAndForwardBuffer _forwardBuffer;
    private readonly GatewayMetrics _metrics;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<TcpGatewayService> _logger;

    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private long _connectionSequence;
    private TcpListener? _listener;

    public TcpGatewayService(
        GatewayOptions options,
        IEventBackbone backbone,
        IDeviceRegistry registry,
        IDeviceAuthenticator authenticator,
        ITelemetryReplayGuard replayGuard,
        IPositionProjectionStore projectionStore,
        Gt06Adapter adapter,
        IStoreAndForwardBuffer forwardBuffer,
        GatewayMetrics metrics,
        ILoggerFactory loggerFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _backbone = backbone ?? throw new ArgumentNullException(nameof(backbone));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _replayGuard = replayGuard ?? throw new ArgumentNullException(nameof(replayGuard));
        _projectionStore = projectionStore ?? throw new ArgumentNullException(nameof(projectionStore));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _forwardBuffer = forwardBuffer ?? throw new ArgumentNullException(nameof(forwardBuffer));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<TcpGatewayService>();
    }

    /// <summary>
    /// The port the listener actually bound to. Meaningful once <see cref="StartAsync"/> has
    /// returned; when <see cref="GatewayOptions.ListenPort"/> is 0 this is the ephemeral port
    /// the OS chose.
    /// </summary>
    public int BoundPort { get; private set; }

    /// <summary>Exposes the counters for health endpoints and tests.</summary>
    public GatewayMetrics Metrics => _metrics;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Bind address is config-driven and fails closed to loopback (see
        // GatewayOptions.ResolveListenAddress). Deployments set Gateway:ListenAddress to
        // 0.0.0.0/:: to become a reachable device edge; dev and tests stay on loopback.
        var bindAddress = _options.ResolveListenAddress();
        _listener = new TcpListener(bindAddress, _options.ListenPort);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        if (!IPAddress.IsLoopback(bindAddress))
            _logger.LogWarning(
                "Telematics gateway bound to NON-loopback interface {BindAddress}: it is now reachable by external peers. Ensure a firewall, the gateway secret, and per-device auth are in place.",
                bindAddress);

        _logger.LogInformation(
            "Telematics gateway listening on {Endpoint} (protocol {Protocol}, max {MaxConnections} connections, idle timeout {IdleTimeout}).",
            _listener.LocalEndpoint, Gt06Adapter.ProtocolName, _options.MaxConnections, _options.IdleTimeout);

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TcpListener listener = _listener
            ?? throw new InvalidOperationException("Listener was not bound; StartAsync did not run.");

        while (!stoppingToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break; // Normal shutdown.
            }
            catch (ObjectDisposedException)
            {
                break; // Listener stopped underneath us during shutdown.
            }
            catch (InvalidOperationException)
            {
                break; // Listener is no longer started (shutdown).
            }
            catch (SocketException ex)
            {
                // A single failed accept (peer reset during handshake, transient FD pressure) must
                // never end the accept loop...
                if (stoppingToken.IsCancellationRequested)
                    break;

                _logger.LogWarning(ex, "Accept failed; continuing to listen.");

                // ...but it must not become a hot spin either, if the fault is persistent.
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            // ── Connection quota ────────────────────────────────────────────────
            // Claim the slot atomically, then check: if we are over, hand it straight back.
            // Shedding deterministically beats accepting work we cannot serve.
            if (_metrics.IncrementActiveConnections() > _options.MaxConnections)
            {
                _metrics.DecrementActiveConnections();
                _metrics.IncrementConnectionsRejectedQuota();

                _logger.LogWarning(
                    "Connection quota ({MaxConnections}) reached; shedding connection from {RemoteEndpoint}.",
                    _options.MaxConnections, SafeRemote(client));

                try
                {
                    client.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to close a shed connection.");
                }

                continue;
            }

            _metrics.IncrementConnectionsAccepted();
            TelematicsInstrumentation.ActiveConnections.Add(1, ActiveConnectionLabels);

            long id = Interlocked.Increment(ref _connectionSequence);
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

            Task task = Task.Run(() => connection.RunAsync(stoppingToken), CancellationToken.None);

            // Register BEFORE attaching cleanup. A connection can run to completion synchronously
            // (a malformed flood does exactly that — identify, reject, close, all without yielding).
            // If cleanup could run before this line, its TryRemove would find nothing and the
            // completed task would be stranded in the map forever: an unbounded leak under exactly
            // the hostile conditions the gateway has to survive. Registering first makes the
            // removal strictly ordered after the insert.
            _connections[id] = task;

            _ = task.ContinueWith(
                completed =>
                {
                    _metrics.DecrementActiveConnections();
                    TelematicsInstrumentation.ActiveConnections.Add(-1, ActiveConnectionLabels);
                    _connections.TryRemove(id, out _);

                    // RunAsync is total, so this should be unreachable — but an unobserved
                    // faulted task must never be allowed to disappear silently.
                    if (completed.IsFaulted)
                        _logger.LogError(completed.Exception, "Connection task faulted unexpectedly.");
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        _logger.LogInformation("Gateway accept loop stopped.");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Shutdown is a <b>drain</b>, not a kill: stop accepting, let in-flight connections finish
    /// publishing what they already decoded (they observe the stopping token and wind down), and
    /// only then let the host go. Bounded by <see cref="GatewayOptions.DrainTimeout"/> so a wedged
    /// peer cannot hold the process open forever.
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Gateway stopping; draining {ActiveConnections} active connection(s).",
            _metrics.ActiveConnections);

        // Cancel FIRST, close the listener second. AcceptTcpClientAsync honours the stopping
        // token, so cancelling unblocks the accept loop cleanly and it exits. Closing the
        // listener first would instead race the loop into repeated faulted accepts before it
        // ever observes the token. Once base.StopAsync returns, the loop has exited and the
        // drain set can no longer grow.
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _listener?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Listener stop threw during shutdown.");
        }

        Task[] pending = _connections.Values.ToArray();
        if (pending.Length > 0)
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(_options.DrainTimeout, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("All connections drained cleanly.");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning(
                    "Drain timed out after {DrainTimeout}; {ActiveConnections} connection(s) abandoned.",
                    _options.DrainTimeout, _metrics.ActiveConnections);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Shutdown cancelled while draining connections.");
            }
        }

        _logger.LogInformation(
            "Gateway stopped. Accepted={Accepted}, FramesDecoded={Frames}, EventsPublished={Published}, UnknownDeviceRejections={Unknown}, MalformedDropped={Malformed}, Buffered={Buffered}.",
            _metrics.ConnectionsAccepted,
            _metrics.FramesDecoded,
            _metrics.EventsPublished,
            _metrics.UnknownDeviceRejections,
            _metrics.MalformedConnectionsDropped,
            _metrics.PublishFailuresBuffered);
    }

    /// <summary>Bounded labels for the active-connections gauge: gateway instance + protocol.</summary>
    private static TagList ActiveConnectionLabels => new()
    {
        { TelematicsInstrumentation.MetricLabels.Gateway, GatewayInstance },
        { TelematicsInstrumentation.MetricLabels.Protocol, "gt06" },
    };

    private static string SafeRemote(TcpClient client)
    {
        try
        {
            return client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
