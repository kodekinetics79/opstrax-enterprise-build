using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Opstrax.Telematics.Contracts.Eventing;
using Opstrax.Telematics.Contracts.Identity;
using Opstrax.Telematics.Gateway;
using Opstrax.Telematics.Gateway.Buffering;
using Opstrax.Telematics.Gateway.Eventing;
using Opstrax.Telematics.Gateway.Identity;
using Opstrax.Telematics.Gateway.Infrastructure;
using Opstrax.Telematics.Gateway.Observability;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Security.Auth;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

// ── Composition root for the Opstrax Telematics Device Edge Gateway ────────────
//
// Production is intentionally a separate, fail-closed branch: no seeded registry,
// process-local replay state, in-memory backbone, or volatile outage buffer is reachable from it.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GatewayOptions options =
    builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>()
    ?? new GatewayOptions();

builder.Services.AddSingleton(options);

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
bool production = builder.Environment.IsProduction();

if (production)
{
    if (string.IsNullOrWhiteSpace(platformRegistryDb))
        throw new InvalidOperationException(
            "Production requires ConnectionStrings:PlatformRegistry (or Gateway:RegistryConnectionString).");
    if (string.IsNullOrWhiteSpace(telematicsDb))
        throw new InvalidOperationException(
            "Production requires ConnectionStrings:Telematics (or Gateway:PostgresConnectionString).");
    if (!TryReadKey32(protectedQueueKey, out byte[] queueKey))
        throw new InvalidOperationException(
            "Production requires Gateway:StoreForwardEncryptionKey as a base64-encoded 32-byte key.");

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

// OpenTelemetry tracer + meter providers (no-op exporter unless an OTLP endpoint is configured).
builder.Services.AddTelematicsObservability(builder.Configuration);

// Closes the durability loop: drains the store-and-forward buffer and republishes parked events
// in per-device order with bounded backoff once the backbone recovers.
builder.Services.AddSingleton<StoreAndForwardReplayOptions>();
builder.Services.AddHostedService<StoreAndForwardReplayService>();

builder.Services.AddSingleton<GatewayMetrics>();
builder.Services.AddHostedService<TcpGatewayService>();

IHost host = builder.Build();

if (!production)
{
    host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Opstrax.Telematics.Gateway.Startup")
        .LogWarning(
            "Development/test gateway: seeded ownership and PROCESS-LOCAL, NON-DURABLE " +
            "event/replay/projection/store-forward implementations are active. Production refuses " +
            "to start unless all durable registry, ledger and encryption settings are supplied.");
}

// Resolve the providers once so they are constructed (and disposed on shutdown) and begin
// listening to the gateway's ActivitySource/Meter. Recording works without them; they are what
// export.
host.Services.GetService<TracerProvider>();
host.Services.GetService<MeterProvider>();

await host.RunAsync().ConfigureAwait(false);

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
