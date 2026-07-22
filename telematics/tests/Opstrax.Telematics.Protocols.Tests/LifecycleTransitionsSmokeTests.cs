using Opstrax.Telematics.Contracts.Lifecycle;
using Opstrax.Telematics.Protocols.Gt06;

namespace Opstrax.Telematics.Protocols.Tests;

/// <summary>
/// Cross-project smoke tests proving the Contracts and Gt06 projects are referenced and
/// compile-clean. The full GT06 decode suite lives in <see cref="Gt06AdapterTests"/>.
/// </summary>
public class LifecycleTransitionsSmokeTests
{
    [Fact]
    public void Provisioned_never_transitions_directly_to_online()
    {
        Assert.False(LifecycleTransitions.CanTransition(
            DeviceLifecycleState.Provisioned, DeviceLifecycleState.Online));
    }

    [Fact]
    public void Gt06_adapter_declares_protocol_name()
    {
        Assert.Equal("GT06", Gt06Adapter.ProtocolName);
    }
}
