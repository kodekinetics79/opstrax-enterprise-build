using Microsoft.Extensions.Logging;

namespace Opstrax.Telematics.Gateway.Edge;

/// <summary>
/// Admission control for the public device edge: the set of IMEIs permitted to open a session.
/// Merges the inline configuration list with an optional newline-delimited file that is re-read
/// when it changes, so commissioning a device never needs a restart.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is and is not.</b> It is not authentication — the IMEI is self-asserted and
/// spoofable, and this type is careful never to imply otherwise. It is a <em>reachability</em>
/// control: on a public TCP port, it is the difference between "any scanner can open a session,
/// stream frames, and make us do work" and "only provisioned units get past the first frame".
/// Everything downstream (replay defence, OpsTrax's own device resolution and per-gateway tenant
/// scoping) still applies to whoever gets through.
/// </para>
/// <para>
/// <b>Fail closed, in both directions.</b> An empty allowlist admits nothing. A configured file
/// that cannot be read admits nothing either, and keeps saying so — it does <b>not</b> fall back
/// to the last good contents, because a silently stale allowlist would keep admitting a device
/// that was just revoked. A deleted or unreadable file is an outage, and an outage on an
/// admission control must deny.
/// </para>
/// <para><b>Thread-safety.</b> Safe for concurrent readers; a reload swaps an immutable set.</para>
/// </remarks>
internal sealed class ImeiAllowlist
{
    private readonly AllowlistOptions _options;
    private readonly ILogger _logger;
    private readonly object _reloadGate = new();
    private readonly IReadOnlySet<string> _configured;
    private readonly Func<DateTime> _clock;

    private volatile IReadOnlySet<string> _effective;
    private DateTime _lastCheckUtc = DateTime.MinValue;
    private DateTime? _lastFileWriteUtc;
    private bool _fileFaulted;

    /// <summary>Creates an allowlist over the configured inline entries and optional file.</summary>
    /// <param name="options">Allowlist configuration.</param>
    /// <param name="logger">Receives load/reload/refusal diagnostics. Never receives a full IMEI.</param>
    /// <param name="clock">UTC clock seam for tests. Defaults to <see cref="DateTime.UtcNow"/>.</param>
    public ImeiAllowlist(AllowlistOptions options, ILogger logger, Func<DateTime>? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? (() => DateTime.UtcNow);

        _configured = Normalize(options.Imeis);
        _effective = _configured;

        // Load the file eagerly so a misconfigured path is a startup-visible warning rather than a
        // surprise on the first device connection at 3am.
        ReloadIfStale(force: true);
    }

    /// <summary>Number of IMEIs currently admitted. Exposed for health output and startup logging.</summary>
    public int Count => _effective.Count;

    /// <summary>Whether a file is configured but currently unreadable — in which case nothing is admitted.</summary>
    public bool IsFileFaulted
    {
        get { lock (_reloadGate) return _fileFaulted; }
    }

    /// <summary>
    /// Whether <paramref name="imei"/> may open a session. Returns <see langword="false"/> for
    /// null, blank, or non-listed identifiers.
    /// </summary>
    public bool IsAllowed(string? imei)
    {
        ReloadIfStale(force: false);

        string? normalized = NormalizeOne(imei);
        return normalized is not null && _effective.Contains(normalized);
    }

    /// <summary>
    /// Re-reads the allowlist file when its modification time has changed and the
    /// <see cref="AllowlistOptions.ReloadInterval"/> has elapsed since the last check.
    /// </summary>
    private void ReloadIfStale(bool force)
    {
        if (string.IsNullOrWhiteSpace(_options.Path))
            return;

        DateTime now = _clock();

        lock (_reloadGate)
        {
            if (!force && now - _lastCheckUtc < _options.ReloadInterval)
                return;

            _lastCheckUtc = now;

            DateTime? writeTime;
            try
            {
                var info = new FileInfo(_options.Path!);
                writeTime = info.Exists ? info.LastWriteTimeUtc : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                FaultFile($"could not stat allowlist file: {ex.Message}");
                return;
            }

            if (writeTime is null)
            {
                FaultFile("allowlist file does not exist");
                return;
            }

            // Unchanged since the last successful read, and that read succeeded: nothing to do.
            if (!force && !_fileFaulted && writeTime == _lastFileWriteUtc)
                return;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(_options.Path!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                FaultFile($"could not read allowlist file: {ex.Message}");
                return;
            }

            var merged = new HashSet<string>(_configured, StringComparer.Ordinal);
            foreach (string line in lines)
            {
                string? entry = NormalizeOne(StripComment(line));
                if (entry is not null) merged.Add(entry);
            }

            bool recovered = _fileFaulted;
            _fileFaulted = false;
            _lastFileWriteUtc = writeTime;
            _effective = merged;

            _logger.Log(
                recovered ? LogLevel.Warning : LogLevel.Information,
                "IMEI allowlist loaded from {Path}: {Count} device(s) admitted{Recovered}.",
                _options.Path, merged.Count, recovered ? " (recovered from a read failure)" : string.Empty);
        }
    }

    /// <summary>
    /// Marks the file unreadable and drops back to the inline entries only. Caller holds the gate.
    /// </summary>
    private void FaultFile(string reason)
    {
        // Deliberately NOT keeping the last good file contents: see the fail-closed note on the type.
        _effective = _configured;
        _lastFileWriteUtc = null;

        if (_fileFaulted) return; // Log the transition, not every subsequent check.
        _fileFaulted = true;

        _logger.LogError(
            "IMEI allowlist file {Path} is unusable ({Reason}); only the {Count} inline configured IMEI(s) are admitted. " +
            "File-listed devices are refused until this is fixed.",
            _options.Path, reason, _configured.Count);
    }

    /// <summary>Drops a <c>#</c> comment and any trailing inline annotation after whitespace.</summary>
    private static string? StripComment(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) return null;

        int cut = trimmed.IndexOfAny(new[] { ' ', '\t', '#' });
        return cut < 0 ? trimmed : trimmed[..cut];
    }

    private static IReadOnlySet<string> Normalize(IEnumerable<string>? entries)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (entries is null) return set;

        foreach (string entry in entries)
        {
            string? normalized = NormalizeOne(entry);
            if (normalized is not null) set.Add(normalized);
        }

        return set;
    }

    /// <summary>
    /// Canonicalises one entry to bare digits. Operators paste IMEIs from spreadsheets and
    /// invoices, where they arrive with spaces or hyphens; a device that reports
    /// <c>862464068456321</c> must still match an allowlist line written <c>86-246406-845632-1</c>.
    /// Anything with a non-digit that is not separator punctuation is rejected outright rather
    /// than silently coerced.
    /// </summary>
    private static string? NormalizeOne(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        Span<char> digits = stackalloc char[32];
        int length = 0;

        foreach (char c in raw)
        {
            if (char.IsAsciiDigit(c))
            {
                if (length == digits.Length) return null; // Absurdly long: not an IMEI.
                digits[length++] = c;
            }
            else if (c is not (' ' or '\t' or '-' or '_' or '.' or ':'))
            {
                return null;
            }
        }

        // IMEIs are 15 digits, IMEISVs 16; some vendor serials registered in eld_devices are
        // shorter. Accept 8..20 so a serial-registered unit works, reject obvious junk.
        return length is >= 8 and <= 20 ? new string(digits[..length]) : null;
    }
}
