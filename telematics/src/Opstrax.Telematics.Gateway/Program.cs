using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Opstrax.Telematics.Contracts.Adapters;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Gateway;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Edge;
using Opstrax.Telematics.Gateway.Eventing;
using Opstrax.Telematics.Gateway.Forwarding;
using Opstrax.Telematics.Gateway.Identity;
using Opstrax.Telematics.Gateway.Infrastructure;
using Opstrax.Telematics.Gateway.Observability;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Security.Auth;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;
using Opstrax.Telematics.Protocols.PacificTrack;

// ── Composition root for the Opstrax Telematics Device Edge Gateway ────────────
//
// TWO TOPOLOGIES, chosen by Gateway:Edge:Egress.
//
//   Postgres (default) — the gateway holds Neon credentials and writes the live-map projection
//     itself. Correct when it runs inside the same trust boundary as the database.
//
//   Https — the PUBLIC edge. The gateway holds no database credentials at all; it decodes,
//     gates the IMEI, suppresses replays, and forwards each fix to OpsTrax over HTTPS with a
//     per-gateway HMAC. This is the mode for a VPS on the open internet, where a compromised
//     box must not be able to reach the database, and where OpsTrax stays the single authority
//     on which tenant owns which device.
//
// Production is intentionally a separate, fail-closed branch in either topology: no seeded
// registry, process-local replay state, in-memory backbone, or volatile outage buffer is
// reachable from it.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GatewayOptions options =
    builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>()
    ?? new GatewayOptions();

builder.Services.AddSingleton(options);

EdgeOptions edge =
    builder.Configuration.GetSection(EdgeOptions.SectionName).Get<EdgeOptions>()
    ?? new EdgeOptions();

builder.Services.AddSingleton(edge);
builder.Services.AddSingleton<GatewayMetrics>();

// OpenTelemetry tracer + meter providers (no-op exporter unless an OTLP endpoint is configured).
builder.Services.AddTelematicsObservability(builder.Configuration);

if (edge.Egress == EgressMode.Https)
{
    ConfigureForwardingEdge(builder, options, edge);
    await RunAsync(builder).ConfigureAwait(false);
    return;
}

// Protocol decoders are pure and stateless — safe to share across every connection. The per-frame
// ceiling is driven from GatewayOptions so the decoder's frame bound and the connection's
// reassembly-buffer bound come from ONE configuration source and cannot silently diverge.
builder.Services.AddSingleton<Gt06Adapter>(_ => new Gt06Adapter(options.MaxFrameBytes));

// GT06 login frames do not carry the canonical HMAC proof this authenticator requires and this
// raw TcpClient listener has no client-certificate transport. Keep cryptographic modes explicitly
// unavailable here; deploy a proof-capable adapter/verified TLS terminator before enabling them.
builder.Services.AddSingleton<ICredentialKeyResolver, RawTcpCryptographyUnavailableResolver>();
builder.Services.AddSingleton<IDeviceAuthenticator, DefaultDeviceAuthenticator>();

string? telematicsDb =
    builder.Configuration.GetConnectionString("Telematics")
    ?? builder.Configuration["Gateway:PostgresConnectionString"];
string? platformRegistryDb =
    builder.Configuration.GetConnectionString("PlatformRegistry")
    ?? builder.Configuration["Gateway:RegistryConnectionString"];
string? protectedQueueKey = builder.Configuration["Gateway:StoreForwardEncryptionKey"];
bool protectedEnvironment = GatewayEnvironment.IsProtected(builder.Environment.EnvironmentName);

if (protectedEnvironment)
{
    if (string.IsNullOrWhiteSpace(platformRegistryDb))
        throw new InvalidOperationException(
            "Production and Staging require ConnectionStrings:PlatformRegistry (or Gateway:RegistryConnectionString).");
    if (string.IsNullOrWhiteSpace(telematicsDb))
        throw new InvalidOperationException(
            "Production and Staging require ConnectionStrings:Telematics (or Gateway:PostgresConnectionString).");
    if (!TryReadKey32(protectedQueueKey, out byte[] queueKey))
        throw new InvalidOperationException(
            "Production and Staging require Gateway:StoreForwardEncryptionKey as a base64-encoded 32-byte key.");

    builder.Services.AddSingleton<IDeviceRegistry>(_ => new PostgresDeviceRegistry(platformRegistryDb));
    builder.Services.AddSingleton<ITelemetryReplayGuard>(_ =>
        new PostgresReplayGuard(telematicsDb!, serialModulus: 65_536));
    builder.Services.AddSingleton<IPositionProjectionStore>(_ => new PostgresPositionProjectionStore(telematicsDb!));
    builder.Services.AddSingleton<IEventBackbone>(_ => new PostgresEventBackbone(telematicsDb!));
    var durableBuffer = new PostgresStoreAndForwardBuffer(telematicsDb!, queueKey);
    System.Security.Cryptography.CryptographicOperations.ZeroMemory(queueKey);
    builder.Services.AddSingleton<IStoreAndForwardBuffer>(durableBuffer);
    builder.Services.AddSingleton(new ProductionStorageReadinessOptions(platformRegistryDb, telematicsDb));
    builder.Services.AddHostedService<ProductionStorageReadinessService>();
}
else
{
    builder.Services.AddSingleton<IDeviceRegistry>(_ => InMemoryDeviceRegistry.SeededDefault());
    builder.Services.AddSingleton<ITelemetryReplayGuard>(_ => new InMemoryReplayGuard(serialModulus: 65536));
    builder.Services.AddSingleton<IPositionProjectionStore, InMemoryPositionProjectionStore>();
    builder.Services.AddSingleton<IEventBackbone>(_ => new InMemoryEventBackbone());
    builder.Services.AddSingleton<IStoreAndForwardBuffer>(_ => new InMemoryStoreAndForwardBuffer());
}

// Closes the durability loop: drains the store-and-forward buffer and republishes parked events
// in per-device order with bounded backoff once the backbone recovers.
builder.Services.AddSingleton<StoreAndForwardReplayOptions>();
builder.Services.AddHostedService<StoreAndForwardReplayService>();

// Bound explicitly rather than by DI constructor selection: TcpGatewayService now has two
// constructors, and letting the container pick between them would make the topology depend on
// which dependencies happened to be registered.
builder.Services.AddSingleton<IConnectionHandlerFactory>(sp => new CanonicalConnectionHandlerFactory(
    sp.GetRequiredService<IEventBackbone>(),
    sp.GetRequiredService<IDeviceRegistry>(),
    sp.GetRequiredService<IDeviceAuthenticator>(),
    sp.GetRequiredService<ITelemetryReplayGuard>(),
    sp.GetRequiredService<IPositionProjectionStore>(),
    sp.GetRequiredService<Gt06Adapter>(),
    sp.GetRequiredService<IStoreAndForwardBuffer>(),
    options,
    sp.GetRequiredService<GatewayMetrics>(),
    sp.GetRequiredService<ILoggerFactory>()));

AddGatewayListener(builder, options);

await RunAsync(
    builder,
    protectedEnvironment
        ? null
        : "Development/test gateway: seeded ownership and PROCESS-LOCAL, NON-DURABLE " +
          "event/replay/projection/store-forward implementations are active. Protected environments refuse " +
          "to start unless all durable registry, ledger and encryption settings are supplied.")
    .ConfigureAwait(false);

// ── Local composition helpers ──────────────────────────────────────────────────

// Composes the PUBLIC HTTPS forwarding edge: protocol adapters, IMEI allowlist, edge-local
// replay defence, the signed forwarder, and the durable outbox with its drain service.
//
// Nothing registered here can reach a database. That is the security property the mode exists
// for, and it is enforced by construction rather than by configuration discipline.
static void ConfigureForwardingEdge(HostApplicationBuilder builder, GatewayOptions options, EdgeOptions edge)
{
    // Refuse to boot on bad forwarding config. An edge that accepts tracker connections it can
    // never deliver looks healthy from outside while quietly filling its outbox.
    if (HttpsOpstraxForwarder.Validate(edge.Forward) is { } problem)
        throw new InvalidOperationException(problem);

    builder.Services.AddSingleton<EdgeMetrics>();

    builder.Services.AddSingleton(sp => new ImeiAllowlist(
        edge.Allowlist,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<ImeiAllowlist>()));

    builder.Services.AddSingleton<IOpstraxForwarder>(sp => new HttpsOpstraxForwarder(
        edge.Forward,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<HttpsOpstraxForwarder>()));

    builder.Services.AddSingleton<IForwardOutbox>(sp => new FileForwardOutbox(
        edge.Outbox,
        sp.GetRequiredService<EdgeMetrics>(),
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<FileForwardOutbox>()));

    // Edge-local replay defence. It is deliberately in-process: OpsTrax owns the DURABLE ledger,
    // keyed on the canonical HMAC signature, and remains the authority across edge restarts and
    // across multiple edges. This guard's job is only to stop a device's own retransmissions from
    // costing a round trip each.
    builder.Services.AddSingleton<ITelemetryReplayGuard>(_ => new InMemoryReplayGuard(serialModulus: 65_536));

    builder.Services.AddSingleton(sp => BuildProtocolRouter(
        edge, options, sp.GetRequiredService<ILoggerFactory>()));

    builder.Services.AddSingleton(edge.Outbox);
    builder.Services.AddHostedService<OutboxDrainService>();

    builder.Services.AddSingleton<IConnectionHandlerFactory>(sp => new ForwardingConnectionHandlerFactory(
        sp.GetRequiredService<ProtocolRouter>(),
        sp.GetRequiredService<ImeiAllowlist>(),
        sp.GetRequiredService<ITelemetryReplayGuard>(),
        sp.GetRequiredService<IOpstraxForwarder>(),
        sp.GetRequiredService<IForwardOutbox>(),
        options,
        edge,
        sp.GetRequiredService<GatewayMetrics>(),
        sp.GetRequiredService<EdgeMetrics>(),
        sp.GetRequiredService<ILoggerFactory>()));

    AddGatewayListener(builder, options);
}

// Builds the adapter set this edge offers, and fails closed when none is enabled.
static ProtocolRouter BuildProtocolRouter(EdgeOptions edge, GatewayOptions options, ILoggerFactory loggerFactory)
{
    var adapters = new List<IProtocolAdapter>();

    if (edge.Protocols.Gt06)
        adapters.Add(new Gt06Adapter(options.MaxFrameBytes));

    if (edge.Protocols.PacificTrack.Enabled)
    {
        var host = new PacificTrackParserHost(
            edge.Protocols.PacificTrack,
            loggerFactory.CreateLogger<PacificTrackParserHost>());

        // The host owns a child process; AppDomain exit is the last chance to reap it, since the
        // router itself is a singleton with no disposal hook.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => host.Dispose();

        adapters.Add(new PacificTrackAdapter(host.Parser));
    }

    if (adapters.Count == 0)
        throw new InvalidOperationException(
            "No protocol adapters are enabled. Set Gateway:Edge:Protocols:Gt06 and/or " +
            "Gateway:Edge:Protocols:PacificTrack:Enabled — an edge that decodes nothing would accept " +
            "tracker connections and silently discard every frame.");

    return new ProtocolRouter(adapters);
}

// Registers the TCP listener over whichever IConnectionHandlerFactory the topology chose.
static void AddGatewayListener(HostApplicationBuilder builder, GatewayOptions options)
{
    builder.Services.AddSingleton(sp => new TcpGatewayService(
        options,
        sp.GetRequiredService<IConnectionHandlerFactory>(),
        sp.GetRequiredService<GatewayMetrics>(),
        sp.GetRequiredService<ILoggerFactory>()));

    builder.Services.AddHostedService(sp => sp.GetRequiredService<TcpGatewayService>());
}

// Builds and runs the host, emitting an optional startup warning first.
static async Task RunAsync(HostApplicationBuilder builder, string? startupWarning = null)
{
    IHost host = builder.Build();

    if (startupWarning is not null)
        host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Opstrax.Telematics.Gateway.Startup")
            .LogWarning("{Warning}", startupWarning);

    // Resolve the providers once so they are constructed (and disposed on shutdown) and begin
    // listening to the gateway's ActivitySource/Meter. Recording works without them; they are what
    // export.
    host.Services.GetService<TracerProvider>();
    host.Services.GetService<MeterProvider>();

    await host.RunAsync().ConfigureAwait(false);
}

static bool TryReadKey32(string? configured, out byte[] key)
{
    key = Array.Empty<byte>();
    if (string.IsNullOrWhiteSpace(configured)) return false;
    try
    {
        key = Convert.FromBase64String(configured);
        if (key.Length == 32) return true;
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        key = Array.Empty<byte>();
        return false;
    }
    catch (FormatException)
    {
        return false;
    }
}

/// <summary>
/// Assembly entry-point marker. Declared <see langword="public"/> and
/// <see langword="partial"/> so integration tests can reference the gateway
/// assembly's <c>Program</c> type.
/// </summary>
public partial class Program
{
}

internal static class GatewayEnvironment
{
    internal static bool IsProtected(string? environmentName) =>
        string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, Environments.Staging, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Explicit fail-closed boundary for the current raw GT06 listener. This is not a placeholder that
/// production silently upgrades: raw GT06 supplies no HMAC proof to verify, so dereferencing a key
/// would add secret exposure without adding authentication. A future proof-capable protocol adapter
/// replaces this registration together with the login-context construction.
/// </summary>
internal sealed class RawTcpCryptographyUnavailableResolver : ICredentialKeyResolver
{
    public ValueTask<byte[]?> ResolveHmacKeyAsync(
        CredentialMaterial credential,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<byte[]?>(null);
}
