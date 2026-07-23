namespace Opstrax.Telematics.Contracts.Adapters;

/// <summary>
/// The result of an adapter inspecting an opening byte sequence to decide whether it
/// speaks that protocol. Returned by <see cref="IProtocolAdapter.TryIdentify"/> during
/// protocol auto-detection on a freshly accepted connection.
/// </summary>
/// <remarks>
/// <see cref="Confidence"/> lets the gateway arbitrate when several adapters claim a
/// stream: the highest-confidence match wins. Confidence is clamped to [0,1] by the
/// factory methods; construct via <see cref="Match"/> / <see cref="NoMatch"/> rather
/// than the parameterized constructor for clarity.
/// </remarks>
public readonly record struct ProtocolMatch
{
    /// <summary>Creates a match result. Prefer <see cref="Match"/> / <see cref="NoMatch"/>.</summary>
    /// <param name="isMatch">Whether the bytes are recognised as this protocol.</param>
    /// <param name="confidence">Match confidence in [0,1]; forced to 0 when <paramref name="isMatch"/> is false.</param>
    /// <param name="needMoreData">
    /// <see langword="true"/> when the adapter cannot yet decide because the buffer is
    /// shorter than the identifying prefix; the gateway should await more bytes and retry.
    /// </param>
    public ProtocolMatch(bool isMatch, double confidence, bool needMoreData)
    {
        IsMatch = isMatch;
        NeedMoreData = needMoreData;
        var c = confidence < 0 ? 0 : confidence > 1 ? 1 : confidence;
        Confidence = isMatch ? c : 0d;
    }

    /// <summary>Whether the inspected bytes are recognised as this adapter's protocol.</summary>
    public bool IsMatch { get; }

    /// <summary>Confidence of the match in [0,1]; always 0 when <see cref="IsMatch"/> is false.</summary>
    public double Confidence { get; }

    /// <summary>Whether more bytes are required before a decision can be made.</summary>
    public bool NeedMoreData { get; }

    /// <summary>A positive match at the given confidence (default full confidence).</summary>
    public static ProtocolMatch Match(double confidence = 1.0) => new(true, confidence, needMoreData: false);

    /// <summary>A definitive non-match.</summary>
    public static ProtocolMatch NoMatch() => new(false, 0d, needMoreData: false);

    /// <summary>Undecided: the buffer is too short to identify the protocol yet.</summary>
    public static ProtocolMatch Incomplete() => new(false, 0d, needMoreData: true);
}
