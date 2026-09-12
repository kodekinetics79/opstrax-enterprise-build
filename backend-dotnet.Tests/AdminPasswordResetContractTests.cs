namespace Opstrax.Tests;

public sealed class AdminPasswordResetContractTests
{
    private static readonly string Source = File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "../../../../backend-dotnet/Controllers/EndpointMappings.cs")));

    [Fact]
    public void RouteIsASeparateTenantAdminRecoveryAction()
    {
        Assert.Contains("app.MapPost(\"/api/admin/users/{id:long}/reset-password\", AdminUserResetPassword)", Source, StringComparison.Ordinal);
        var handler = Handler();
        Assert.Contains("RequireAdminPermission(http, audit, \"users:manage\"", handler, StringComparison.Ordinal);
        Assert.Contains("id == GetUserId(http)", handler, StringComparison.Ordinal);
        Assert.Contains("GetScopedUser(http, db, id, ct)", handler, StringComparison.Ordinal);
        Assert.Contains("Direct password reset is available only for active users", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetEnforcesTenantPolicyAndRevokesEveryExistingCredentialPath()
    {
        var handler = Handler();
        Assert.Contains("securitySettings.GetAsync(companyId, ct)", handler, StringComparison.Ordinal);
        Assert.Contains("PasswordPolicyService.ValidatePassword", handler, StringComparison.Ordinal);
        Assert.Contains("RunInSystemTransactionAsync", handler, StringComparison.Ordinal);
        Assert.True(handler.IndexOf("GetScopedUser(http, db, id, ct)", StringComparison.Ordinal)
            < handler.IndexOf("RunInSystemTransactionAsync", StringComparison.Ordinal),
            "The system credential transaction must follow authenticated tenant validation.");
        Assert.Contains("WHERE id=@id AND company_id=@companyId AND status='Active'", handler, StringComparison.Ordinal);
        Assert.Contains("WHERE user_id=@id AND company_id=@companyId", handler, StringComparison.Ordinal);
        Assert.Contains("password_hash=@hash", handler, StringComparison.Ordinal);
        Assert.Contains("demo_password=''", handler, StringComparison.Ordinal);
        Assert.Contains("failed_login_attempts=0", handler, StringComparison.Ordinal);
        Assert.Contains("locked_until=NULL", handler, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM user_sessions", handler, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM password_reset_tokens", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetIsAuditedWithoutReturningOrEmailingThePassword()
    {
        var handler = Handler();
        Assert.Contains("user.password.reset_by_admin", handler, StringComparison.Ordinal);
        Assert.Contains("delivery = \"administrator_set\"", handler, StringComparison.Ordinal);
        Assert.Contains("no email was sent", handler, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EmailDeliveryService", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("newPassword }", handler, StringComparison.Ordinal);
    }

    private static string Handler()
    {
        var start = Source.IndexOf("internal static async Task<IResult> AdminUserResetPassword", StringComparison.Ordinal);
        var end = Source.IndexOf("// ── Session management", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Tenant-admin password reset handler could not be isolated.");
        return Source[start..end];
    }
}
