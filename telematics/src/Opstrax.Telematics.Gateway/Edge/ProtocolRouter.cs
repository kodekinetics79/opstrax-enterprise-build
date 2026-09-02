using Opstrax.Telematics.Contracts.Adapters;

namespace Opstrax.Telematics.Gateway.Edge;

/// <summary>The router's verdict on a connection's opening bytes.</summary>
/// <param name="Adapter">The adapter that won arbitration, or null.</param>
/// <param name="NeedMoreData">True when no adapter can decide yet and more bytes should be buffered.</param>
/// <param name="Confidence">Winning confidence in [0,1]; 0 when nothing matched.</param>
internal readonly record struct ProtocolSelection(IProtocolAdapter? Adapter, bool NeedMoreData, double Confidence)
{
    /// <summary><see langword="true"/> when an adapter claimed the stream.</summary>
    public bool IsMatch => Adapter is not null;
}

/// <summary>
/// Picks the protocol adapter for a connection from its opening bytes, so one public port can
/// serve mixed hardware — a GT06-family tracker and a Pacific Track unit dialling the same
/// host:port.
/// </summary>
/// <remarks>
/// <para>
/// <b>Highest confidence wins, and a tie is a refusal.</b> Two adapters claiming the same opening
/// bytes with equal confidence means the fingerprint is genuinely ambiguous. Picking either one
/// would mean decoding a truck's position with the wrong vendor's field layout — which yields
/// coordinates that are wrong but entirely plausible, the single worst failure mode in this
/// subsystem and the one <c>docs/telematics/pt40/pt40-fingerprint.md</c> is written to prevent.
/// Refusing is recoverable; a silently mis-decoded fleet is not.
/// </para>
/// <para>
/// <b>Registration order does not decide anything.</b> There is no first-match-wins fallthrough,
/// deliberately: it would make correctness depend on the order adapters happened to be registered
/// in, and would let a permissive adapter shadow a stricter one.
/// </para>
/// </remarks>
internal sealed class ProtocolRouter
{
    private readonly IReadOnlyList<IProtocolAdapter> _adapters;

    /// <summary>Creates a router over the installed adapters.</summary>
    /// <param name="adapters">Adapters to arbitrate between. Must contain at least one.</param>
    public ProtocolRouter(IReadOnlyList<IProtocolAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        if (adapters.Count == 0)
            throw new ArgumentException("The edge needs at least one protocol adapter.", nameof(adapters));

        _adapters = adapters;
    }

    /// <summary>The adapters this router arbitrates between.</summary>
    public IReadOnlyList<IProtocolAdapter> Adapters => _adapters;

    /// <summary>Human-readable list of installed adapters, for startup logging and metric labels.</summary>
    public string Describe() => string.Join(", ", _adapters.Select(a => $"{a.Metadata.Name} v{a.Metadata.Version}"));

    /// <summary>Arbitrates over the buffered opening bytes.</summary>
    public ProtocolSelection Select(ReadOnlySpan<byte> opening)
    {
        IProtocolAdapter? best = null;
        double bestConfidence = 0;
        bool tied = false;
        bool anyNeedsMore = false;

        foreach (IProtocolAdapter adapter in _adapters)
        {
            ProtocolMatch match = adapter.TryIdentify(opening);

            if (match.NeedMoreData)
            {
                anyNeedsMore = true;
                continue;
            }

            if (!match.IsMatch) continue;

            if (best is null || match.Confidence > bestConfidence)
            {
                best = adapter;
                bestConfidence = match.Confidence;
                tied = false;
            }
            else if (match.Confidence == bestConfidence)
            {
                tied = true;
            }
        }

        if (tied) return new ProtocolSelection(null, NeedMoreData: false, Confidence: bestConfidence);
        if (best is not null) return new ProtocolSelection(best, NeedMoreData: false, bestConfidence);

        // Nothing matched. Keep buffering only while some adapter is still undecided; otherwise
        // this is a definitive non-match and the caller closes the connection.
        return new ProtocolSelection(null, anyNeedsMore, Confidence: 0);
    }
}
