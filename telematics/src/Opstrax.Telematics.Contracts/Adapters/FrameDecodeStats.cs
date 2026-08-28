namespace Opstrax.Telematics.Contracts.Adapters;

/// <summary>
/// Per-call framing statistics reported alongside a decode, so an operator can tell a quiet
/// device apart from a device whose frames are arriving corrupted.
/// </summary>
/// <remarks>
/// <para>
/// These are the counts for <em>one</em> <see cref="IProtocolAdapter.Decode(System.ReadOnlySpan{byte}, out int, out FrameDecodeStats)"/>
/// call only — the adapter stays pure and accumulates nothing. The gateway folds them into its
/// process-wide <c>Interlocked</c> counters.
/// </para>
/// <para>
/// <b>Semantics.</b> <see cref="FramesRead"/> counts complete, correctly framed frames that the
/// decoder stepped over, whether or not their checksum verified. <see cref="CrcFailures"/> counts
/// the subset whose checksum did not verify and which therefore produced <b>no</b> message. So
/// <c>FramesRead - CrcFailures</c> equals the number of decoded messages returned, and a
/// CRC-invalid frame can never be counted as decoded.
/// </para>
/// </remarks>
/// <param name="FramesRead">Complete frames stepped over by this call (CRC-valid and CRC-invalid).</param>
/// <param name="CrcFailures">Frames rejected by the checksum; no message was emitted for any of them.</param>
public readonly record struct FrameDecodeStats(int FramesRead, int CrcFailures);
