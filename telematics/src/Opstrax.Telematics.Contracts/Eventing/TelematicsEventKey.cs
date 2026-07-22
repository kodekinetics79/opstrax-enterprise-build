using System.Globalization;

namespace Opstrax.Telematics.Contracts.Eventing;

/// <summary>
/// Builds and interprets the partition/ordering key used on the telematics backbone. The key
/// is the mechanism that delivers both guarantees the fabric depends on at once:
/// <list type="bullet">
///   <item><description>
///     <b>Ordering</b> — every event for a given device lands on the same partition, so a
///     consumer sees that device's events in production order (a fix never overtakes an earlier
///     fix for the same vehicle).
///   </description></item>
///   <item><description>
///     <b>Isolation</b> — the key is prefixed with tenant and company, so one tenant's traffic
///     can never collide with, reorder, or be mistaken for another's on a shared topic.
///   </description></item>
/// </list>
/// </summary>
/// <remarks>
/// The device segment is what pins ordering to a single vehicle/device; the tenant and company
/// segments in front of it keep partitions cleanly separated per owner. Same input always
/// yields the same key, and therefore (via <see cref="Partition"/>) the same partition — which
/// is exactly the Kafka/Redpanda co-partitioning contract.
/// </remarks>
public static class TelematicsEventKey
{
    private const char Separator = ':';

    /// <summary>
    /// Builds the canonical ordering/partition key <c>{tenant}:{company}:{device}</c> for a
    /// device-scoped event. This is the key to pass to
    /// <see cref="IEventBackbone.PublishAsync{T}"/> for any telemetry, position, health,
    /// trip, diagnostic, safety or media event.
    /// </summary>
    /// <param name="tenantId">Registry-resolved owning tenant.</param>
    /// <param name="companyId">Registry-resolved owning company.</param>
    /// <param name="deviceId">Fabric device id whose events must stay ordered together.</param>
    public static string ForDevice(Guid tenantId, long companyId, string deviceId) =>
        string.Concat(
            tenantId.ToString("N"),
            Separator.ToString(),
            companyId.ToString(CultureInfo.InvariantCulture),
            Separator.ToString(),
            deviceId);

    /// <summary>
    /// Builds a command-scoped ordering key <c>{tenant}:{company}:cmd:{deviceId}</c>. Command
    /// lifecycle events (requested → dispatched → acknowledged/failed) key on the target
    /// <paramref name="deviceId"/> so the whole lifecycle of commands aimed at one device stays
    /// ordered on a single partition.
    /// </summary>
    /// <param name="tenantId">Registry-resolved owning tenant.</param>
    /// <param name="companyId">Registry-resolved owning company.</param>
    /// <param name="deviceId">Target device the command lifecycle is ordered against.</param>
    public static string ForCommand(Guid tenantId, long companyId, string deviceId) =>
        string.Concat(
            tenantId.ToString("N"),
            Separator.ToString(),
            companyId.ToString(CultureInfo.InvariantCulture),
            Separator.ToString(),
            "cmd",
            Separator.ToString(),
            deviceId);

    /// <summary>
    /// Maps a key to a stable partition index in <c>[0, partitionCount)</c> using a
    /// deterministic FNV-1a hash. Deterministic across processes and runs (unlike
    /// <see cref="string.GetHashCode()"/>), so a key always co-partitions the same way — the
    /// property the in-memory dev backbone and a real Kafka/Redpanda cluster must share.
    /// </summary>
    /// <param name="key">A key produced by <see cref="ForDevice"/> or <see cref="ForCommand"/>.</param>
    /// <param name="partitionCount">The topic's partition count; must be positive.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null/empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partitionCount"/> is not positive.</exception>
    public static int Partition(string key, int partitionCount)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Partition key must be non-empty.", nameof(key));
        if (partitionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(partitionCount), partitionCount,
                "Partition count must be positive.");

        // FNV-1a 32-bit — deterministic and dependency-free.
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        uint hash = offsetBasis;
        foreach (char c in key)
        {
            hash ^= c;
            hash *= prime;
        }

        return (int)(hash % (uint)partitionCount);
    }
}
