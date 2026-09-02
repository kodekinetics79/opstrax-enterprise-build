using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Protocols.PacificTrack;

namespace Opstrax.Telematics.Gateway.Edge;

/// <summary>
/// Starts and supervises Pacific Track's official parser as a child process, and exposes it as an
/// <see cref="IPacificTrackParser"/> through <see cref="StdioParserBridge"/>. This is how the
/// vendor's Python or Java parser is used from the .NET edge without reimplementing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A missing parser degrades, it does not crash.</b> If the child cannot start, the host logs
/// the failure and hands back <see cref="UnavailablePacificTrackParser"/>. The gateway keeps
/// serving GT06 hardware, and Pacific Track devices are refused at protocol arbitration and
/// counted — visibly broken for PT, rather than an edge that is down for everyone.
/// </para>
/// <para>
/// <b>The child's stderr is drained.</b> A child that writes diagnostics and is never read will
/// block on a full pipe and stop answering decode requests — a hang that looks like a protocol
/// problem and is not. Its output is forwarded to the gateway log instead.
/// </para>
/// </remarks>
internal sealed class PacificTrackParserHost : IDisposable
{
    private readonly ILogger _logger;
    private Process? _process;
    private StdioParserBridge? _bridge;

    /// <summary>Starts the configured parser, or falls back to the fail-closed stub.</summary>
    /// <param name="options">Pacific Track wiring.</param>
    /// <param name="logger">Receives startup, fault and child-stderr diagnostics.</param>
    public PacificTrackParserHost(PacificTrackOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(options.ParserCommand))
        {
            _logger.LogWarning(
                "Pacific Track support is enabled but no parser is installed " +
                "(Gateway:Edge:Protocols:PacificTrack:ParserCommand is unset). The adapter is registered fail-closed: " +
                "PT devices will be refused, never decoded by another vendor's adapter. " +
                "See src/Opstrax.Telematics.Protocols.PacificTrack/README.md.");

            Parser = UnavailablePacificTrackParser.Instance;
            return;
        }

        Parser = TryStart(options) ?? UnavailablePacificTrackParser.Instance;
    }

    /// <summary>The parser to register behind <see cref="PacificTrackAdapter"/>. Never null.</summary>
    public IPacificTrackParser Parser { get; }

    private IPacificTrackParser? TryStart(PacificTrackOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.ParserCommand!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        foreach (string argument in options.ParserArguments)
            startInfo.ArgumentList.Add(argument);

        if (!string.IsNullOrWhiteSpace(options.ParserWorkingDirectory))
            startInfo.WorkingDirectory = options.ParserWorkingDirectory;

        try
        {
            _process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogError(ex,
                "Could not start the Pacific Track parser '{Command}'. PT devices will be refused.",
                options.ParserCommand);
            return null;
        }

        if (_process is null)
        {
            _logger.LogError(
                "Starting the Pacific Track parser '{Command}' produced no process. PT devices will be refused.",
                options.ParserCommand);
            return null;
        }

        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogWarning("Pacific Track parser: {Message}", e.Data);
        };
        _process.BeginErrorReadLine();

        _bridge = new StdioParserBridge(
            _process.StandardInput,
            _process.StandardOutput,
            options.ParserVersion,
            options.ParserTimeout,
            onFault: reason => _logger.LogCritical(
                "Pacific Track parser bridge faulted and will not be reused: {Reason}. " +
                "PT devices are refused until the gateway restarts.", reason));

        _logger.LogInformation(
            "Pacific Track parser started: {Command} {Arguments} (version {Version}, {Timeout} per call).",
            options.ParserCommand, string.Join(' ', options.ParserArguments),
            string.IsNullOrEmpty(options.ParserVersion) ? "unreported" : options.ParserVersion,
            options.ParserTimeout);

        return _bridge;
    }

    /// <summary>Stops the bridge and terminates the child process.</summary>
    public void Dispose()
    {
        _bridge?.Dispose();

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2_000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            _logger.LogDebug(ex, "Pacific Track parser process did not stop cleanly.");
        }
        finally
        {
            _process?.Dispose();
        }
    }
}
