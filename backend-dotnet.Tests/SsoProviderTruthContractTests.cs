using System.Reflection;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class SsoProviderTruthContractTests
{
    private static readonly MethodInfo ValidateDto = typeof(SsoConnectionService)
        .GetMethod("ValidateDto", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("SsoConnectionService.ValidateDto not found");

    [Fact]
    public void Oidc_connection_can_be_enabled()
    {
        Invoke(new SsoConnectionDto
        {
            ProviderType = "oidc",
            DisplayName = "Enterprise OIDC",
            IssuerOrEntityId = "https://idp.example.test",
            ClientId = "client-1",
            Enabled = true,
        });
    }

    [Fact]
    public void Saml_configuration_can_be_saved_only_when_disabled()
    {
        Invoke(new SsoConnectionDto
        {
            ProviderType = "saml",
            DisplayName = "Future SAML",
            IssuerOrEntityId = "urn:example:idp",
            ClientId = "future-saml",
            Enabled = false,
        });
    }

    [Fact]
    public void Saml_connection_cannot_be_enabled_before_real_flow_exists()
    {
        var ex = Assert.Throws<TargetInvocationException>(() => InvokeRaw(new SsoConnectionDto
        {
            ProviderType = "saml",
            DisplayName = "Unsupported Active SAML",
            IssuerOrEntityId = "urn:example:idp",
            ClientId = "saml-active",
            Enabled = true,
        }));

        var argument = Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Contains("SAML SSO is not yet an implemented authentication path", argument.Message, StringComparison.Ordinal);
    }

    private static void Invoke(SsoConnectionDto dto)
    {
        try
        {
            InvokeRaw(dto);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void InvokeRaw(SsoConnectionDto dto)
        => ValidateDto.Invoke(null, new object[] { dto });
}
