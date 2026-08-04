using System.Diagnostics;

namespace Opstrax.Telematics.Gateway.Observability;

/// <summary>Stable span names for the gateway pipeline. These are what you filter on in Tempo/Jaeger.</summary>
public static class TelematicsSpans
{
    /// <summary>Lifetime of one accepted TCP/UDP connection (SERVER). Child spans nest under it per packet.</summary>
    public const string DeviceConnection = "device-connection";

    /// <summary>Reading one framed packet off the socket and driving it through the pipeline (parent per packet).</summary>
    public const string PacketReceive = "packet-receive";

    /// <summary>Adapter parses raw bytes into a normalized reading.</summary>
    public const string Decode = "decode";

    /// <summary>IMEI/device-key → device_id → vehicle_id → tenant/company_id (HMAC verified here).</summary>
    public const string Identity = "identity";

    /// <summary>Checksum, freshness/replay window, nonce uniqueness, ordering, tenant-bind, timestamp bounds.</summary>
    public const string Validation = "validation";

    /// <summary>Writes the accepted reading onto the backbone (PRODUCER); injects traceparent into the envelope.</summary>
    public const string EventPublish = "event-publish";
}

/// <summary>
/// Span-attribute keys, in OTel semantic-convention style (<c>namespace.key</c>). These are the
/// dotted attribute keys carried on spans and exemplars — deliberately distinct from the bounded
/// metric label keys in <see cref="TelematicsInstrumentation.MetricLabels"/>.
/// </summary>
public static class TelematicsAttributes
{
    /// <summary><c>telematics.correlation_id</c> — stable per-packet id that crosses the queue boundary.</summary>
    public const string CorrelationId = "telematics.correlation_id";

    /// <summary><c>telematics.connection_id</c> — gateway socket/session id; groups a connection's packets.</summary>
    public const string ConnectionId = "telematics.connection_id";

    /// <summary><c>telematics.event_id</c> — identity of the published event; de-dup key.</summary>
    public const string EventId = "telematics.event_id";

    /// <summary><c>telematics.protocol</c> — wire protocol (pt40|gt06|j1939|obd2|mqtt).</summary>
    public const string Protocol = "telematics.protocol";

    /// <summary><c>telematics.rejection_reason</c> — set only on rejection; drives otel.status_code=ERROR.</summary>
    public const string RejectionReason = "telematics.rejection_reason";

    /// <summary><c>tenant.id</c> — resolved tenant. Required on identity→publish spans.</summary>
    public const string TenantId = "tenant.id";

    /// <summary><c>company.id</c> — resolved company_id within the tenant.</summary>
    public const string CompanyId = "company.id";

    /// <summary><c>device.id</c> — resolved device id.</summary>
    public const string DeviceId = "device.id";

    /// <summary><c>device.imei</c> — lookup key only, never a credential; useful pre-identity.</summary>
    public const string DeviceImei = "device.imei";

    /// <summary><c>vehicle.id</c> — resolved OpsTrax vehicle.</summary>
    public const string VehicleId = "vehicle.id";

    /// <summary><c>adapter.name</c> — e.g. gt06, pt40.</summary>
    public const string AdapterName = "adapter.name";

    /// <summary><c>adapter.version</c> — pins a decode regression to a release.</summary>
    public const string AdapterVersion = "adapter.version";

    /// <summary><c>gateway.instance</c> — gateway instance/pod id.</summary>
    public const string GatewayInstance = "gateway.instance";
}

/// <summary>
/// Threads one packet through the gateway's span chain. A <see cref="PipelineTrace"/> owns the
/// per-packet parent span (<see cref="TelematicsSpans.PacketReceive"/>) and mints the
/// child stage spans (<c>decode → identity → validation → event-publish</c>) beneath it, stamping
/// the correlation/connection/tenant/company/device/adapter context onto every span so a single
/// trace answers "where did this fix stop, and who did it belong to".
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership is applied late, never guessed.</b> Tenant/company/device/vehicle are unknown
/// until the <c>identity</c> stage resolves them from the registry. Call
/// <see cref="ResolveOwnership"/> once resolved; it back-fills the parent and every subsequent
/// child span. This mirrors the honest-attribution rule the rest of the gateway follows — an
/// unattributed packet carries no tenant tag rather than a fabricated one.
/// </para>
/// <para>
/// <b>Null-safe by construction.</b> When no listener/provider is sampling, every
/// <c>StartActivity</c> returns <see langword="null"/> and every method here is a cheap no-op —
/// the caller writes the same code on the hot path whether or not tracing is enabled.
/// </para>
/// <para>
/// <b>This is an isolated primitive.</b> It is intentionally not yet wired into
/// <c>GatewayConnection</c>'s read loop; that wiring is a later increment. The type is fully
/// unit-testable in isolation through an in-memory <see cref="ActivityListener"/>.
/// </para>
/// </remarks>
public sealed class PipelineTrace : IDisposable
{
    private readonly Activity? _root;

    private PipelineTrace(Activity? root, Guid correlationId, string connectionId, string protocol)
    {
        _root = root;
        CorrelationId = correlationId;
        ConnectionId = connectionId;
        Protocol = protocol;
    }

    /// <summary>Stable per-packet correlation id (survives the queue boundary as a plain attribute).</summary>
    public Guid CorrelationId { get; }

    /// <summary>Gateway socket/session id for this connection.</summary>
    public string ConnectionId { get; }

    /// <summary>Wire protocol for this packet (e.g. <c>gt06</c>).</summary>
    public string Protocol { get; }

    /// <summary>Adapter name, once <see cref="SetAdapter"/> has been called; otherwise <see langword="null"/>.</summary>
    public string? AdapterName { get; private set; }

    /// <summary>Adapter version, once <see cref="SetAdapter"/> has been called.</summary>
    public string? AdapterVersion { get; private set; }

    /// <summary>Resolved tenant, once <see cref="ResolveOwnership"/> has been called.</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>Resolved company_id, once <see cref="ResolveOwnership"/> has been called.</summary>
    public long? CompanyId { get; private set; }

    /// <summary>Resolved device id, once <see cref="ResolveOwnership"/> has been called.</summary>
    public string? DeviceId { get; private set; }

    /// <summary>Resolved vehicle id, when the device was bound to a vehicle.</summary>
    public long? VehicleId { get; private set; }

    /// <summary>The parent <see cref="TelematicsSpans.PacketReceive"/> activity (may be <see langword="null"/>).</summary>
    public Activity? Root => _root;

    /// <summary>
    /// Starts the per-packet parent span. <paramref name="imei"/> is a pre-identity lookup key
    /// (never a credential) and is attached when known.
    /// </summary>
    /// <param name="correlationId">Stable per-packet correlation id.</param>
    /// <param name="connectionId">Gateway socket/session id.</param>
    /// <param name="protocol">Wire protocol (e.g. <c>gt06</c>).</param>
    /// <param name="imei">Claimed IMEI, if already read off the frame; lookup key only.</param>
    /// <param name="gatewayInstance">Gateway instance/pod id, for span-side attribution.</param>
    public static PipelineTrace StartPacketReceive(
        Guid correlationId,
        string connectionId,
        string protocol,
        string? imei = null,
        string? gatewayInstance = null)
    {
        Activity? root = TelematicsInstrumentation.ActivitySource.StartActivity(
            TelematicsSpans.PacketReceive, ActivityKind.Server);

        if (root is not null)
        {
            root.SetTag(TelematicsAttributes.CorrelationId, correlationId.ToString());
            root.SetTag(TelematicsAttributes.ConnectionId, connectionId);
            root.SetTag(TelematicsAttributes.Protocol, protocol);
            if (imei is not null)
                root.SetTag(TelematicsAttributes.DeviceImei, imei);
            if (gatewayInstance is not null)
                root.SetTag(TelematicsAttributes.GatewayInstance, gatewayInstance);
        }

        return new PipelineTrace(root, correlationId, connectionId, protocol);
    }

    /// <summary>Starts the <c>decode</c> child span (INTERNAL), stamped with the base context.</summary>
    public Activity? StartDecode() => StartChild(TelematicsSpans.Decode, ActivityKind.Internal);

    /// <summary>Starts the <c>identity</c> child span (INTERNAL), stamped with the base context.</summary>
    public Activity? StartIdentity() => StartChild(TelematicsSpans.Identity, ActivityKind.Internal);

    /// <summary>Starts the <c>validation</c> child span (INTERNAL), stamped with the base context.</summary>
    public Activity? StartValidation() => StartChild(TelematicsSpans.Validation, ActivityKind.Internal);

    /// <summary>Starts the <c>event-publish</c> child span (PRODUCER), stamped with the base context.</summary>
    public Activity? StartPublish() => StartChild(TelematicsSpans.EventPublish, ActivityKind.Producer);

    /// <summary>Records the decoding adapter's name/version on the parent and future child spans.</summary>
    public void SetAdapter(string adapterName, string adapterVersion)
    {
        AdapterName = adapterName;
        AdapterVersion = adapterVersion;
        _root?.SetTag(TelematicsAttributes.AdapterName, adapterName);
        _root?.SetTag(TelematicsAttributes.AdapterVersion, adapterVersion);
    }

    /// <summary>
    /// Back-fills the registry-resolved ownership onto the parent span. Call once, after the
    /// identity stage resolves it. Values are the authoritative isolation scope — never taken
    /// from the packet.
    /// </summary>
    public void ResolveOwnership(Guid tenantId, long companyId, string deviceId, long? vehicleId = null)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        DeviceId = deviceId;
        VehicleId = vehicleId;

        if (_root is null)
            return;

        _root.SetTag(TelematicsAttributes.TenantId, tenantId.ToString());
        _root.SetTag(TelematicsAttributes.CompanyId, companyId);
        _root.SetTag(TelematicsAttributes.DeviceId, deviceId);
        if (vehicleId is { } v)
            _root.SetTag(TelematicsAttributes.VehicleId, v);
    }

    /// <summary>Records the published event id on the parent span (the de-dup key echoed into storage).</summary>
    public void SetEventId(Guid eventId) => _root?.SetTag(TelematicsAttributes.EventId, eventId.ToString());

    /// <summary>
    /// Marks a span (and the parent) as a rejection: stamps <c>rejection_reason</c> and sets
    /// <c>otel.status_code=ERROR</c> so "packets that vanish" stay visible in Tempo. Rejections
    /// are first-class spans, not dropped work.
    /// </summary>
    public void MarkRejected(Activity? span, string reason, string? description = null)
    {
        span?.SetTag(TelematicsAttributes.RejectionReason, reason);
        span?.SetStatus(ActivityStatusCode.Error, description ?? reason);
        _root?.SetTag(TelematicsAttributes.RejectionReason, reason);
        _root?.SetStatus(ActivityStatusCode.Error, description ?? reason);
    }

    /// <summary>
    /// Builds the bounded metric label set for exemplar-carrying instrument increments. Only the
    /// bounded dimensions from <c>metrics.md</c> are emitted — <c>company_id</c> only when it has
    /// been resolved, and never any high-cardinality identifier. Pass a <paramref name="reason"/>
    /// on a rejection instrument.
    /// </summary>
    public TagList MetricLabels(string? reason = null)
    {
        var tags = new TagList
        {
            { TelematicsInstrumentation.MetricLabels.Protocol, Protocol },
        };

        if (AdapterName is not null)
            tags.Add(TelematicsInstrumentation.MetricLabels.Adapter, AdapterName);
        if (AdapterVersion is not null)
            tags.Add(TelematicsInstrumentation.MetricLabels.AdapterVersion, AdapterVersion);
        if (CompanyId is { } company)
            tags.Add(TelematicsInstrumentation.MetricLabels.CompanyId, company);
        if (reason is not null)
            tags.Add(TelematicsInstrumentation.MetricLabels.Reason, reason);

        return tags;
    }

    /// <summary>Ends the parent packet span. Child spans are disposed by their own <c>using</c> scopes.</summary>
    public void Dispose() => _root?.Dispose();

    private Activity? StartChild(string name, ActivityKind kind)
    {
        // Parent is Activity.Current (the packet-receive span) via ambient propagation.
        Activity? child = TelematicsInstrumentation.ActivitySource.StartActivity(name, kind);
        if (child is null)
            return null;

        child.SetTag(TelematicsAttributes.CorrelationId, CorrelationId.ToString());
        child.SetTag(TelematicsAttributes.ConnectionId, ConnectionId);
        child.SetTag(TelematicsAttributes.Protocol, Protocol);

        if (AdapterName is not null)
            child.SetTag(TelematicsAttributes.AdapterName, AdapterName);
        if (AdapterVersion is not null)
            child.SetTag(TelematicsAttributes.AdapterVersion, AdapterVersion);
        if (TenantId is { } tenant)
            child.SetTag(TelematicsAttributes.TenantId, tenant.ToString());
        if (CompanyId is { } company)
            child.SetTag(TelematicsAttributes.CompanyId, company);
        if (DeviceId is not null)
            child.SetTag(TelematicsAttributes.DeviceId, DeviceId);
        if (VehicleId is { } vehicle)
            child.SetTag(TelematicsAttributes.VehicleId, vehicle);

        return child;
    }
}
