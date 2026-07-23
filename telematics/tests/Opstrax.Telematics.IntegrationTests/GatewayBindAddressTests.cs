using System.Net;
using Opstrax.Telematics.Gateway;

namespace Opstrax.Telematics.IntegrationTests;

/// <summary>
/// Guards the deploy knob that turns the gateway from a loopback-only dev process into a
/// reachable device edge (root blocker #1 for the PT40 pilot). The security-critical
/// property is that binding must FAIL CLOSED: a wider interface is only ever bound when
/// explicitly and validly configured — never as the accidental result of a bad value.
/// </summary>
public class GatewayBindAddressTests
{
    [Fact]
    public void Default_binds_loopback_so_nothing_is_exposed_without_opt_in()
    {
        var options = new GatewayOptions();

        Assert.Equal("127.0.0.1", options.ListenAddress);
        Assert.True(IPAddress.IsLoopback(options.ResolveListenAddress()));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("10.0.1.7")]
    public void Explicit_valid_address_is_honored_for_deployment(string configured)
    {
        var options = new GatewayOptions { ListenAddress = configured };

        Assert.Equal(IPAddress.Parse(configured), options.ResolveListenAddress());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-ip")]
    [InlineData("999.999.999.999")]
    [InlineData(null)]
    public void Unparseable_or_empty_fails_closed_to_loopback_never_widening_exposure(string? configured)
    {
        var options = new GatewayOptions { ListenAddress = configured! };

        Assert.True(IPAddress.IsLoopback(options.ResolveListenAddress()),
            "A bad ListenAddress must never bind a wider interface than configured.");
    }
}
