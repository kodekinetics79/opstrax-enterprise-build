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
using Opstrax.Telematics.Gateway.Identity;
using Opstrax.Telematics.Gateway.Observability;
using Opstrax.Telematics.Gateway.Projection;
using Opstrax.Telematics.Gateway.Security.Auth;
using Opstrax.Telematics.Gateway.Security.Replay;
using Opstrax.Telematics.Protocols.Gt06;

// ── Composition root for the Opstrax Telematics Device Edge Gateway ────────────
//
// Every dependency below is an interface the gateway depends on and a swappable
// implementation it does not know about. The in-memory implementations here are the
// dev/test doubles: production swaps in Kafka/Redpanda, the Postgres device table, and
// the durable Postgres replay/projection stores WITHOUT the framing loop changing.
//
// No secrets are configured here. Device credentials never reach this process —
// the registry returns an opaque CredentialHandle pointing at the vault.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

GatewayOptions options =
    builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>()
    ?? new GatewayOptions();

builder.Services.AddSingleton(options);

// Protocol decoders are pure and stateless — safe to share across every connection. The per-frame
// ceiling is driven from GatewayOptions so the decoder's frame bound and the connection's
// reassembly-buffer bound come from ONE configuration source and cannot silently diverge.
builder.Services.AddSingleton<Gt06Adapter>(_ => new Gt06Adapter(options.MaxFrameBytes));

// Dev/test backbone. Production: a Kafka/Redpanda-backed IEventBackbone.
builder.Services.AddSingleton<IEventBackbone>(_ => new InMemoryEventBackbone());

// The ONLY source of device ownership AND per-device trust policy. Production: the Postgres-backed
// registry. The gateway reads the device's policy from here per device — it never hardcodes one.
builder.Services.AddSingleton<IDeviceRegistry>(_ => InMemoryDeviceRegistry.SeededDefault());

// Trust enforcement after identity resolution. The credential resolver is fail-closed: the dev
// gateway has no vault wired, so any HMAC-mode device is rejected. The GT06 baseline is
// ImeiAllowlistOnly, which never dereferences a key, so this is a no-op on the happy path.
builder.Services.AddSingleton<ICredentialKeyResolver, VaultUnavailableCredentialKeyResolver>();
builder.Services.AddSingleton<IDeviceAuthenticator, DefaultDeviceAuthenticator>();

// Durability of replay/sequence defence AND live-map projection is CONFIG-DRIVEN. With a Telematics
// Postgres connection string these use the shared, durable Postgres-backed stores: the replay window
// becomes the database — it survives a restart and is shared across every gateway instance, closing
// the process-local gap the threat model flags. Without one, the process-local in-memory doubles run
// for dev/test and a loud warning is emitted after build so a non-durable gateway is never mistaken
// for production.
string? telematicsDb =
    builder.Configuration.GetConnectionString("Telematics")
    ?? builder.Configuration["Gateway:PostgresConnectionString"];
bool durableStores = !string.IsNullOrWhiteSpace(telematicsDb);

if (durableStores)
{
    // PostgresReplayGuard's UNIQUE(device_id, serial, content_hash) is the durable, cross-instance
    // dedup primitive; feed it a monotonic ingest sequence (it compares serials as 64-bit values).
    builder.Services.AddSingleton<ITelemetryReplayGuard>(_ => new PostgresReplayGuard(telematicsDb!));
    builder.Services.AddSingleton<IPositionProjectionStore>(_ => new PostgresPositionProjectionStore(telematicsDb!));
}
else
{
    // The modulus matches GT06's 16-bit information serial so a legitimate counter wrap (65 535 → 0)
    // reads as forward progress, not out-of-order. NON-DURABLE: forgotten on restart, not shared.
    builder.Services.AddSingleton<ITelemetryReplayGuard>(_ => new InMemoryReplayGuard(serialModulus: 65536));
    builder.Services.AddSingleton<IPositionProjectionStore, InMemoryPositionProjectionStore>();
}

// OpenTelemetry tracer + meter providers (no-op exporter unless an OTLP endpoint is configured).
builder.Services.AddTelematicsObservability(builder.Configuration);

// Durability seam for broker outages. Production: a disk/WAL-backed buffer.
builder.Services.AddSingleton<IStoreAndForwardBuffer>(_ => new InMemoryStoreAndForwardBuffer());

// Closes the durability loop: drains the store-and-forward buffer and republishes parked events
// in per-device order with bounded backoff once the backbone recovers. This is the ONLY consumer
// of IStoreAndForwardBuffer.TryDequeue.
builder.Services.AddSingleton<StoreAndForwardReplayOptions>();
builder.Services.AddHostedService<StoreAndForwardReplayService>();

builder.Services.AddSingleton<GatewayMetrics>();
builder.Services.AddHostedService<TcpGatewayService>();

IHost host = builder.Build();

// Fail loud, not silent: a gateway running the non-durable in-memory replay/projection stores must
// never be mistaken for a production deployment. Surfaced once at startup.
if (!durableStores)
{
    host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Opstrax.Telematics.Gateway.Startup")
        .LogWarning(
            "No 'Telematics' Postgres connection string is configured — replay defence and live-map " +
            "projection are running on PROCESS-LOCAL, NON-DURABLE in-memory stores. Dev/test only: a " +
            "restart forgets the replay window and the guarantee is not shared across instances. Set " +
            "ConnectionStrings:Telematics (or Gateway:PostgresConnectionString) before production use.");
}

// Resolve the providers once so they are constructed (and disposed on shutdown) and begin
// listening to the gateway's ActivitySource/Meter. Recording works without them; they are what
// export.
host.Services.GetService<TracerProvider>();
host.Services.GetService<MeterProvider>();

await host.RunAsync().ConfigureAwait(false);

/// <summary>
/// Assembly entry-point marker. Declared <see langword="public"/> and
/// <see langword="partial"/> so integration tests can reference the gateway
/// assembly's <c>Program</c> type.
/// </summary>
public partial class Program
{
}

/// <summary>
/// The fail-closed credential resolver the dev/local gateway ships with: no vault is wired, so it
/// resolves <b>no</b> key. Per <see cref="ICredentialKeyResolver"/>'s contract, returning
/// <see langword="null"/> makes the authenticator reject any device that requires a cryptographic
/// proof. The honest GT06 baseline (<see cref="DeviceAuthMode.ImeiAllowlistOnly"/>) never
/// dereferences a key, so this never runs on that path. Production swaps in a KMS/vault-backed
/// resolver without touching the framing loop.
/// </summary>
internal sealed class VaultUnavailableCredentialKeyResolver : ICredentialKeyResolver
{
    public ValueTask<byte[]?> ResolveHmacKeyAsync(
        CredentialMaterial credential,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<byte[]?>(null);
}
