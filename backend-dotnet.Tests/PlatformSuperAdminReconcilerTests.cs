using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Pure, DB-free unit tests for the break-glass reconciler's decision logic — the part
// that decides WHETHER to act. The DB write path (RunInSystemScopeAsync → platform_admins)
// is covered by the boot wiring and exercised in integration; here we pin the invariants
// that keep an armed flag safe: it must be opt-in, must refuse weak passwords, and must be
// an idempotent no-op once the account already matches env.
[Trait("Category", "Unit")]
public class PlatformSuperAdminReconcilerTests
{
    // ── Arming: the flag is inert unless it is an explicit truthy token ──────────
    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("YES")]
    public void IsTruthy_Arms_On_Explicit_Truthy_Tokens(string value)
        => Assert.True(PlatformSuperAdminReconciler.IsTruthy(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("tru")]
    [InlineData("2")]
    [InlineData(" true ")] // not trimmed by design — an accidental value must NOT arm it
    public void IsTruthy_Stays_Inert_Otherwise(string? value)
        => Assert.False(PlatformSuperAdminReconciler.IsTruthy(value));

    // ── Password floor: never install a weak bootstrap credential ────────────────
    [Theory]
    [InlineData("Recovery2026!")]     // 13 chars, letter + digit
    [InlineData("aaaaaaaaaaa1")]      // exactly 12, letter + digit
    public void MeetsPasswordPolicy_Accepts_Strong_Passwords(string password)
        => Assert.True(PlatformSuperAdminReconciler.MeetsPasswordPolicy(password));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short1A")]           // too short
    [InlineData("aaaaaaaaaa2")]       // exactly 11 chars (boundary) — one under the 12 floor
    [InlineData("abcdefghijkl")]      // 12 letters, no digit
    [InlineData("123456789012")]      // 12 digits, no letter
    public void MeetsPasswordPolicy_Rejects_Weak_Passwords(string? password)
        => Assert.False(PlatformSuperAdminReconciler.MeetsPasswordPolicy(password));

    // ── Idempotent no-op: a healthy, in-sync bootstrap admin needs no action ─────
    [Fact]
    public void NeedsReconcile_Is_False_For_Healthy_InSync_Account()
        => Assert.False(PlatformSuperAdminReconciler.NeedsReconcile(
            passwordMatches: true, status: "Active", roleKey: "platform_super_admin", hasPendingInvite: false));

    [Fact]
    public void NeedsReconcile_Treats_Status_And_Role_Case_Insensitively()
        => Assert.False(PlatformSuperAdminReconciler.NeedsReconcile(
            passwordMatches: true, status: "active", roleKey: "PLATFORM_SUPER_ADMIN", hasPendingInvite: false));

    // Each individual drift condition must force a reconcile.
    [Theory]
    [InlineData(false, "Active", "platform_super_admin", false)] // env password no longer verifies
    [InlineData(true, "Disabled", "platform_super_admin", false)] // account not Active
    [InlineData(true, "Invited", "platform_super_admin", false)] // still pending first login
    [InlineData(true, "Active", "platform_compliance_admin", false)] // wrong role
    [InlineData(true, "Active", "", false)] // no role at all
    [InlineData(true, "Active", "platform_super_admin", true)] // invite token still outstanding
    public void NeedsReconcile_Is_True_On_Any_Drift(bool pwMatches, string status, string roleKey, bool pendingInvite)
        => Assert.True(PlatformSuperAdminReconciler.NeedsReconcile(pwMatches, status, roleKey, pendingInvite));
}
