using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Gateway.J1939;

/// <summary>
/// Runs Linux <c>candump -L</c> without a shell and admits only timestamped,
/// extended-identifier classic CAN records from the configured interface.
/// </summary>
internal sealed class CandumpJ1939CanFrameSource(
    J1939CanHostOptions options,
    ILogger<CandumpJ1939CanFrameSource> logger) : IJ1939CanFrameSource
{
    private readonly J1939CanHostOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<CandumpJ1939CanFrameSource> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async IAsyncEnumerable<J1939RawCanFrame> ReadSessionAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = BuildStartInfo(_options),
            EnableRaisingEvents = true,
        };

        if (!process.Start())
            throw new InvalidOperationException("The configured CAN acquisition process did not start.");

        Task stderrDrain = DrainAsync(process.StandardError, CancellationToken.None);
        try
        {
            await foreach (BoundedCanRecord record in ReadRecordsAsync(
                               process.StandardOutput,
                               _options.MaximumLineLength,
                               cancellationToken).ConfigureAwait(false))
            {
                if (record.IsOversized)
                {
                    _logger.LogWarning("Rejected one CAN acquisition record: the record exceeded the configured length bound.");
                    continue;
                }

                J1939RawCanFrame? frame;
                try
                {
                    frame = ParseLine(record.Text!, _options);
                }
                catch (J1939CanInputException ex)
                {
                    // Do not log the raw line: it contains physical bus bytes. The bounded reason
                    // is enough to diagnose format failures without leaking capture material.
                    _logger.LogWarning("Rejected one CAN acquisition record: {Reason}", ex.Message);
                    continue;
                }

                yield return frame;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stderrDrain.ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"The CAN acquisition process exited with code {process.ExitCode}.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }

            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (InvalidOperationException) { }
            try { await stderrDrain.ConfigureAwait(false); }
            catch (IOException) { }
        }
    }

    internal static J1939RawCanFrame ParseLine(string line, J1939CanHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(options);

        if (line.Length == 0 || line.Length > options.MaximumLineLength)
            throw new J1939CanInputException("The record length is outside the configured bound.");
        if (line.Any(char.IsControl))
            throw new J1939CanInputException("The record contains control characters.");
        if (line[0] != '(')
            throw new J1939CanInputException("The record is missing the absolute capture timestamp.");

        int timestampEnd = line.IndexOf(')');
        if (timestampEnd <= 1 || timestampEnd + 1 >= line.Length)
            throw new J1939CanInputException("The record has an invalid capture timestamp field.");

        string timestampText = line[1..timestampEnd];
        string[] fields = line[(timestampEnd + 1)..]
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 2)
            throw new J1939CanInputException("The record must contain one interface and one CAN frame.");
        if (!string.Equals(fields[0], options.Interface, StringComparison.Ordinal))
            throw new J1939CanInputException("The record came from a different CAN interface.");

        int separator = fields[1].IndexOf('#');
        if (separator < 0 || fields[1].IndexOf('#', separator + 1) >= 0)
            throw new J1939CanInputException("The record is not a classic CAN data frame.");

        string identifierText = fields[1][..separator];
        string payloadText = fields[1][(separator + 1)..];
        if (identifierText.Length != 8 || !identifierText.All(Uri.IsHexDigit))
            throw new J1939CanInputException("The record does not contain a 29-bit extended CAN identifier.");
        if (payloadText.Length > 16 || payloadText.Length % 2 != 0 || !payloadText.All(Uri.IsHexDigit))
            throw new J1939CanInputException("The classic CAN payload must contain zero to eight complete bytes.");
        if (!uint.TryParse(identifierText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint identifier) ||
            identifier > J1939CanIdentifier.MaximumExtendedIdentifier)
            throw new J1939CanInputException("The CAN identifier exceeds the 29-bit extended range.");

        DateTimeOffset capturedAt = ParseTimestamp(timestampText);
        byte[] payload = Convert.FromHexString(payloadText);
        string captureReference = "candump:sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(line))).ToLowerInvariant();

        return new J1939RawCanFrame(
            identifier,
            IsExtendedIdentifier: true,
            payload,
            capturedAt,
            options.AdapterType,
            options.Interface,
            captureReference);
    }

    /// <summary>
    /// Reads line-delimited process output without ever accumulating more than the configured
    /// record bound. Oversized records are drained through their newline and represented only by
    /// a marker so the following valid record remains independently parseable.
    /// </summary>
    internal static async IAsyncEnumerable<BoundedCanRecord> ReadRecordsAsync(
        TextReader reader,
        int maximumLineLength,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (maximumLineLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLineLength));

        char[] buffer = new char[Math.Min(1024, maximumLineLength + 1)];
        var current = new StringBuilder(Math.Min(maximumLineLength, 256));
        bool oversized = false;

        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            for (int index = 0; index < read; index++)
            {
                char character = buffer[index];
                if (character == '\n')
                {
                    yield return oversized
                        ? new BoundedCanRecord(null, IsOversized: true)
                        : new BoundedCanRecord(current.ToString(), IsOversized: false);
                    current.Clear();
                    oversized = false;
                    continue;
                }

                if (!oversized && current.Length < maximumLineLength)
                    current.Append(character);
                else
                    oversized = true;
            }
        }

        if (oversized || current.Length > 0)
        {
            yield return oversized
                ? new BoundedCanRecord(null, IsOversized: true)
                : new BoundedCanRecord(current.ToString(), IsOversized: false);
        }
    }

    private static ProcessStartInfo BuildStartInfo(J1939CanHostOptions options)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.CandumpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-L");
        startInfo.ArgumentList.Add(options.Interface);
        startInfo.Environment["LC_ALL"] = "C";
        return startInfo;
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        if (!decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal seconds) ||
            seconds < 0)
            throw new J1939CanInputException("The capture timestamp is not a nonnegative Unix timestamp.");

        decimal ticks = decimal.Truncate(seconds * TimeSpan.TicksPerSecond);
        decimal maximumTicks = DateTimeOffset.MaxValue.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        if (ticks > maximumTicks)
            throw new J1939CanInputException("The capture timestamp is outside the supported date range.");

        return DateTimeOffset.UnixEpoch.AddTicks((long)ticks);
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[1024];
        while (await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false) != 0)
        {
            // Intentionally discard adapter stderr. It can contain bus or device material.
        }
    }
}

internal readonly record struct BoundedCanRecord(string? Text, bool IsOversized);

internal sealed class J1939CanInputException(string message) : Exception(message);
