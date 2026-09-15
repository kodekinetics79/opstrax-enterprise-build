namespace Opstrax.Telematics.Gateway.J1939;

/// <summary>
/// Deployment contract for one locally attached SocketCAN acquisition path.
/// The configured registry serial, rather than any CAN address, selects ownership.
/// </summary>
internal sealed class J1939CanHostOptions
{
    internal const string SectionName = "Gateway:J1939Can";

    public bool Enabled { get; set; }
    public string CandumpPath { get; set; } = "/usr/bin/candump";
    public string Interface { get; set; } = string.Empty;
    public string AdapterType { get; set; } = string.Empty;
    public string RegistryDeviceSerial { get; set; } = string.Empty;
    public TimeSpan MaximumCaptureAge { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaximumFutureSkew { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan FreshnessBudget { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan RegistryRefreshInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan RestartDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan TransportSessionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaximumLineLength { get; set; } = 256;
    public double TrustScore { get; set; } = 0.25d;
    public double Confidence { get; set; } = 0.75d;

    internal static string? Validate(J1939CanHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled) return null;

        if (string.IsNullOrWhiteSpace(options.CandumpPath) ||
            !Path.IsPathFullyQualified(options.CandumpPath) ||
            options.CandumpPath.Length > 512 ||
            options.CandumpPath.Any(char.IsControl))
            return "Gateway:J1939Can:CandumpPath must be a bounded absolute executable path.";

        if (!File.Exists(options.CandumpPath))
            return "Gateway:J1939Can:CandumpPath does not exist on this host.";

        if (!ValidToken(options.Interface, 32))
            return "Gateway:J1939Can:Interface must contain 1-32 letters, digits, dot, underscore, colon or hyphen characters.";

        if (!ValidEvidenceLabel(options.AdapterType, 128))
            return "Gateway:J1939Can:AdapterType is required and must be a bounded evidence label.";

        if (!ValidEvidenceLabel(options.RegistryDeviceSerial, 128))
            return "Gateway:J1939Can:RegistryDeviceSerial is required and must match one provisioned registry serial.";

        if (options.MaximumCaptureAge <= TimeSpan.Zero || options.MaximumCaptureAge > TimeSpan.FromHours(24))
            return "Gateway:J1939Can:MaximumCaptureAge must be greater than zero and at most 24 hours.";
        if (options.MaximumFutureSkew < TimeSpan.Zero || options.MaximumFutureSkew > TimeSpan.FromMinutes(1))
            return "Gateway:J1939Can:MaximumFutureSkew must be between zero and one minute.";
        if (options.FreshnessBudget <= TimeSpan.Zero || options.FreshnessBudget > options.MaximumCaptureAge)
            return "Gateway:J1939Can:FreshnessBudget must be positive and cannot exceed MaximumCaptureAge.";
        if (options.RegistryRefreshInterval <= TimeSpan.Zero || options.RegistryRefreshInterval > TimeSpan.FromMinutes(1))
            return "Gateway:J1939Can:RegistryRefreshInterval must be positive and at most one minute.";
        if (options.RestartDelay <= TimeSpan.Zero || options.RestartDelay > TimeSpan.FromMinutes(5))
            return "Gateway:J1939Can:RestartDelay must be positive and at most five minutes.";
        if (options.TransportSessionTimeout <= TimeSpan.Zero || options.TransportSessionTimeout > TimeSpan.FromMinutes(5))
            return "Gateway:J1939Can:TransportSessionTimeout must be positive and at most five minutes.";
        if (options.MaximumLineLength is < 64 or > 4096)
            return "Gateway:J1939Can:MaximumLineLength must be between 64 and 4096 characters.";
        if (!double.IsFinite(options.TrustScore) || options.TrustScore is < 0 or > 1)
            return "Gateway:J1939Can:TrustScore must be finite and inside [0,1].";
        if (!double.IsFinite(options.Confidence) || options.Confidence is < 0 or > 1)
            return "Gateway:J1939Can:Confidence must be finite and inside [0,1].";

        return null;
    }

    private static bool ValidEvidenceLabel(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);

    private static bool ValidToken(string? value, int maximumLength) =>
        !string.IsNullOrEmpty(value) &&
        value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-');
}
