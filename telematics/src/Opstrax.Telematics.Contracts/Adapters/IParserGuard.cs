namespace Opstrax.Telematics.Contracts.Adapters;

/// <summary>
/// Safety limits enforced around untrusted decode work. A hostile or buggy device can
/// send a length header claiming gigabytes, or dribble bytes to hold a connection open
/// forever; the guard bounds both the size a single decode pass may buffer and the wall
/// time it may spend, so a single stream cannot exhaust gateway resources.
/// </summary>
/// <remarks>
/// The guard is a policy object consulted by the gateway's framing loop; adapters
/// themselves stay pure. Implementations MUST fail closed — when a limit is exceeded the
/// frame is dropped and the connection is torn down rather than truncated-and-accepted.
/// </remarks>
public interface IParserGuard
{
    /// <summary>The maximum number of bytes a single un-framed decode buffer may accumulate.</summary>
    int MaxFrameBytes { get; }

    /// <summary>The maximum wall-clock time a single decode pass may run before it is abandoned.</summary>
    TimeSpan DecodeTimeout { get; }

    /// <summary>
    /// Validates a proposed/observed buffer length against <see cref="MaxFrameBytes"/>.
    /// </summary>
    /// <param name="observedLength">The current buffered byte count (or a claimed frame length).</param>
    /// <returns><see langword="true"/> when the length is within budget; <see langword="false"/> when the frame must be rejected.</returns>
    bool IsWithinSizeBudget(int observedLength);
}
