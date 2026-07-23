namespace Opstrax.Telematics.Contracts.Adapters;

/// <summary>
/// A protocol-agnostic downlink command the fabric wishes to send to a device (for
/// example "cut engine", "request location", "set reporting interval"). The adapter
/// translates this intent into vendor wire bytes via
/// <see cref="IProtocolAdapter.EncodeCommand"/>; adapters that cannot express a given
/// command return <see langword="null"/> rather than approximating it.
/// </summary>
/// <param name="Name">The canonical command name, for example <c>"LocationRequest"</c> or <c>"EngineCutoff"</c>.</param>
/// <param name="Arguments">Command parameters keyed by name (may be empty).</param>
public readonly record struct DeviceCommand(
    string Name,
    IReadOnlyDictionary<string, string> Arguments);
