namespace Opstrax.Telematics.Gateway.Edge;

/// <summary>
/// Where a decoded fix goes once the edge has accepted it. This is the one switch that decides
/// whether the gateway is a <em>database writer</em> or a <em>protocol translator</em>.
/// </summary>
public enum EgressMode
{
    /// <summary>
    /// The original topology: the gateway holds Neon credentials and writes
    /// <c>latest_vehicle_positions</c> itself. Correct when the gateway runs inside the same
    /// trust boundary as the database.
    /// </summary>
    Postgres = 0,

    /// <summary>
    /// The public-edge topology: the gateway holds <b>no database credentials at all</b> and
    /// forwards each normalized fix to OpsTrax over HTTPS
    /// (<c>POST /api/telemetry/gps-ingest</c>), signed with a per-gateway HMAC secret.
    /// This is the mode a VPS-hosted, internet-facing gateway should run in — a box reachable by
    /// every scanner on the internet has no business holding a Postgres role.
    /// </summary>
    Https = 1,
}

/// <summary>
/// Configuration for the public TCP device edge, bound from the <c>Gateway:Edge</c> section.
/// </summary>
/// <remarks>
/// Defaults are the safe end of every axis: <see cref="Egress"/> stays <see cref="EgressMode.Postgres"/>
/// so an existing deployment's behaviour is unchanged by upgrading, and the allowlist starts
/// empty, which admits nothing.
/// </remarks>
public sealed class EdgeOptions
{
    /// <summary>The configuration section this type binds from.</summary>
    public const string SectionName = "Gateway:Edge";

    /// <summary>Where accepted fixes are sent. See <see cref="EgressMode"/>.</summary>
    public EgressMode Egress { get; set; } = EgressMode.Postgres;

    /// <summary>IMEI admission control. Enforced before any device is bound to a session.</summary>
    public AllowlistOptions Allowlist { get; set; } = new();

    /// <summary>HTTPS forwarding settings. Required (and only used) when <see cref="Egress"/> is <see cref="EgressMode.Https"/>.</summary>
    public ForwardOptions Forward { get; set; } = new();

    /// <summary>Durable on-disk queue used when OpsTrax is unreachable.</summary>
    public OutboxOptions Outbox { get; set; } = new();

    /// <summary>Which protocol adapters the edge offers to inbound connections.</summary>
    public ProtocolOptions Protocols { get; set; } = new();
}

/// <summary>IMEI admission control for the device edge.</summary>
/// <remarks>
/// <para>
/// An IMEI allowlist is <b>not authentication</b> — an IMEI is a self-asserted, spoofable bearer
/// identifier (see <c>DeviceAuthMode.ImeiAllowlistOnly</c>). What it does buy is real and worth
/// having on a public port: it turns the edge from "anyone may open a session and stream frames"
/// into "only the handful of units we provisioned may", which collapses the surface a mass
/// scanner can reach and keeps unowned traffic out of the outbox and off OpsTrax entirely.
/// </para>
/// </remarks>
public sealed class AllowlistOptions
{
    /// <summary>
    /// IMEIs allowed to connect, set inline in configuration. Merged with <see cref="Path"/>.
    /// </summary>
    public IList<string> Imeis { get; set; } = new List<string>();

    /// <summary>
    /// Optional path to a newline-delimited allowlist file. Blank lines and <c>#</c> comments are
    /// ignored; anything after whitespace on a line is treated as a comment, so
    /// <c>862464068456321  # Khalid PT40-Q</c> is valid. Re-read automatically when its
    /// modification time changes, so commissioning a device does not require a restart.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Minimum interval between file modification-time checks. Bounds stat() syscalls under
    /// connection floods; a newly added IMEI is admitted within this window.
    /// </summary>
    public TimeSpan ReloadInterval { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>HTTPS forwarding to the OpsTrax trusted-gateway ingest endpoint.</summary>
public sealed class ForwardOptions
{
    /// <summary>
    /// Base URL of the OpsTrax API, for example
    /// <c>https://opstrax-enterprise-build.onrender.com</c>. Must be <c>https</c> in production —
    /// the HMAC authenticates the body but does not encrypt it, and the fix itself is the vehicle
    /// location of a real person.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Path of the ingest endpoint. Overridable only for testing against a stub.</summary>
    public string IngestPath { get; set; } = "/api/telemetry/gps-ingest";

    /// <summary>
    /// The gateway credential id, sent as <c>X-Gateway-Id</c>. Provisioned by
    /// <c>POST /api/telemetry/gateways</c>, which binds it to exactly one tenant — a gateway
    /// cannot submit fixes for another tenant's device.
    /// </summary>
    public string GatewayId { get; set; } = string.Empty;

    /// <summary>
    /// The per-gateway HMAC secret shown once at provisioning. <b>Supply via environment
    /// (<c>Gateway__Edge__Forward__Secret</c>) or a secrets file, never a committed appsettings.</b>
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Per-request timeout. Kept short: a tracker connection is waiting behind it.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Optional identifier for the physical edge host, attached to forwarded provenance so a fix
    /// can be traced back to the box that relayed it. Defaults to the machine name.
    /// </summary>
    public string? EdgeInstance { get; set; }
}

/// <summary>The durable on-disk queue that makes an OpsTrax outage a delay rather than data loss.</summary>
/// <remarks>
/// Entries are AES-256-GCM encrypted at rest. The key is deliberately <b>not</b> a property of
/// this options object — options instances get logged, dumped, and bound from committed JSON,
/// which is exactly where a key must never appear. It is read separately from
/// <c>Gateway:StoreForwardEncryptionKey</c> (base64, 32 bytes), supplied via the gateway's
/// environment file — never via command line, whose argv is world-readable. See
/// <c>docs/telematics/security/OUTBOX_KEY_MANAGEMENT.md</c> for provisioning and rotation.
/// </remarks>
public sealed class OutboxOptions
{
    /// <summary>
    /// Directory holding parked payloads. Must be on persistent storage the service user can
    /// write — a tmpfs here silently turns store-and-forward into drop-on-reboot.
    /// </summary>
    public string Path { get; set; } = "outbox";

    /// <summary>
    /// Hard ceiling on parked entries. At the ceiling the <b>oldest</b> entry is discarded to
    /// admit the newest: during a long outage the freshest fix is the one a live map needs, and
    /// an unbounded queue would fill the edge's disk and take the listener down with it.
    /// </summary>
    public int MaxEntries { get; set; } = 50_000;

    /// <summary>How often the drain service retries parked payloads once idle.</summary>
    public TimeSpan DrainInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Backoff applied after a failed drain sweep, so a long outage does not become a hot loop.</summary>
    public TimeSpan FailureBackoff { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Entries older than this are discarded undelivered and counted. OpsTrax rejects fixes older than 30 days.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Version byte (1–255) stamped into every encrypted entry beside the format version,
    /// identifying which <c>Gateway:StoreForwardEncryptionKey</c> sealed it. Bump it together
    /// with the key when rotating, so entries written under the retiring key are attributable;
    /// an entry the current key cannot open is discarded as corrupt (fail closed, bounded loss).
    /// </summary>
    public int EncryptionKeyVersion { get; set; } = 1;
}

/// <summary>Which protocol adapters the edge offers to inbound connections.</summary>
public sealed class ProtocolOptions
{
    /// <summary>Enable the GT06/Concox adapter (39/39 protocol tests pass; ready for GT06-family hardware).</summary>
    public bool Gt06 { get; set; } = true;

    /// <summary>Pacific Track (PT40 / PT40-Q) support, via the vendor's official parser.</summary>
    public PacificTrackOptions PacificTrack { get; set; } = new();
}

/// <summary>
/// Wiring for Pacific Track's official parser. OpsTrax ships the adapter seam, not the decoder —
/// see <c>src/Opstrax.Telematics.Protocols.PacificTrack/README.md</c>.
/// </summary>
public sealed class PacificTrackOptions
{
    /// <summary>
    /// Offer the Pacific Track adapter. With no parser configured the adapter is registered but
    /// fail-closed: it claims no stream, so a PT device is refused and counted rather than
    /// mis-decoded by the GT06 adapter.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Executable that hosts the vendor parser as a child process speaking the
    /// <c>StdioParserBridge</c> line protocol — for example <c>python3</c> or <c>java</c>.
    /// Leave blank when the parser is compiled in-process instead.
    /// </summary>
    public string? ParserCommand { get; set; }

    /// <summary>Arguments passed to <see cref="ParserCommand"/>, for example the bridge script path.</summary>
    public IList<string> ParserArguments { get; set; } = new List<string>();

    /// <summary>Working directory for the parser process. Defaults to the gateway's own.</summary>
    public string? ParserWorkingDirectory { get; set; }

    /// <summary>Vendor parser version, recorded in logs and forwarded provenance.</summary>
    public string ParserVersion { get; set; } = string.Empty;

    /// <summary>Per-call ceiling on the child parser's response before the bridge is declared faulted.</summary>
    public TimeSpan ParserTimeout { get; set; } = TimeSpan.FromSeconds(2);
}
