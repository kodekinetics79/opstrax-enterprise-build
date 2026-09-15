using Opstrax.Telematics.Protocols.J1939;

namespace Opstrax.Telematics.Gateway.J1939;

/// <summary>Streams one bounded acquisition-process session of classic CAN frames.</summary>
internal interface IJ1939CanFrameSource
{
    IAsyncEnumerable<J1939RawCanFrame> ReadSessionAsync(CancellationToken cancellationToken = default);
}
