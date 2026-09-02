using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Pure, DB-free unit tests for the super-admin reconciler's decision logic — the part
// that decides WHETHER to act. The DB write path (RunInSystemScopeAsync → platform_admins)
// is covered by the boot wiring and exercised in integration; here we pin the invariants
// that make an unconditional, every-boot reconcile safe: it must refuse weak passwords, it
// must recover a credential that never reached the DB (the lockout), it must apply a rotated
// env credential, and it must NOT stomp a later self-service password change.
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

    // ── Opt-out: the DB owns the credential only on an explicit off token ────────
    [Theory]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("never")]
    public void IsDisabled_Opts_Out_On_Explicit_Off_Tokens(string value)
        => Assert.True(PlatformSuperAdminReconciler.IsDisabled(value));

    [Theory]
    [InlineData(null)]   // unset is the normal case — reconcile runs
    [InlineData("")]
    [InlineData("true")]
    [InlineData("on")]
    public void IsDisabled_Leaves_Reconcile_Enabled_Otherwise(string? value)
        => Assert.False(PlatformSuperAdminReconciler.IsDisabled(value));

    // ── Account repair: anything that blocks sign-in regardless of the password ──
    [Fact]
    public void NeedsAccountRepair_Is_False_For_Active_Super_Admin()
        => Assert.False(PlatformSuperAdminReconciler.NeedsAccountRepair("Active", "platform_super_admin", hasPendingInvite: false));

    [Theory]
    [InlineData("Disabled", "platform_super_admin", false)] // deactivated
    [InlineData("Invited", "platform_super_admin", false)]  // never completed first login
    [InlineData("Active", "finance_admin", false)]          // wrong role ⇒ 403 on every screen
    [InlineData("Active", "", false)]                       // no role at all
    [InlineData("Active", "platform_super_admin", true)]    // invite token still outstanding
    public void NeedsAccountRepair_Is_True_On_Login_Blocking_State(string status, string roleKey, bool pendingInvite)
        => Assert.True(PlatformSuperAdminReconciler.NeedsAccountRepair(status, roleKey, pendingInvite));

    // ── The core of the permanent fix: when does env overwrite the stored password? ──

    // THE LOCKOUT. No sync has ever been recorded and the declared credential does not
    // verify: the password in Render never reached this database. Env must win, with no
    // flag to arm — this is the case that produced "Invalid credentials" against the exact
    // password the operator had configured.
    [Fact]
    public void ShouldApplyPassword_Recovers_A_Credential_That_Never_Reached_The_Database()
        => Assert.True(PlatformSuperAdminReconciler.ShouldApplyPassword(
            forced: false, recordedFingerprint: null, envFingerprint: "fp-a", passwordMatches: false));

    // First boot on an already-correct account: adopt, do not rewrite.
    [Fact]
    public void ShouldApplyPassword_Adopts_An_Already_Working_Credential()
        => Assert.False(PlatformSuperAdminReconciler.ShouldApplyPassword(
            forced: false, recordedFingerprint: null, envFingerprint: "fp-a", passwordMatches: true));

    // The operator rotated PLATFORM_SUPERADMIN_PASSWORD — the new value must reach the DB
    // on the next deploy instead of silently doing nothing.
    [Fact]
    public void ShouldApplyPassword_Applies_A_Rotated_Env_Credential()
        => Assert.True(PlatformSuperAdminReconciler.ShouldApplyPassword(
            forced: false, recordedFingerprint: "fp-old", envFingerprint: "fp-new", passwordMatches: false));

    // Env unchanged since the last sync ⇒ a stored password that no longer matches is a
    // deliberate in-app change. Running on every boot must never revert it.
    [Fact]
    public void ShouldApplyPassword_Does_Not_Stomp_A_SelfService_Password_Change()
        => Assert.False(PlatformSuperAdminReconciler.ShouldApplyPassword(
            forced: false, recordedFingerprint: "fp-a", envFingerprint: "fp-a", passwordMatches: false));

    [Fact]
    public void ShouldApplyPassword_Is_A_NoOp_When_Everything_Is_In_Sync()
        => Assert.False(PlatformSuperAdminReconciler.ShouldApplyPassword(
            forced: false, recordedFingerprint: "fp-a", envFingerprint: "fp-a", passwordMatches: true));

    // The force override covers the one case the fingerprint cannot: env is unrotated but
    // the in-app password was changed and forgotten.
    [Fact]
    public void ShouldApplyPassword_Force_Overrides_A_Matching_Fingerprint()
        => Assert.True(PlatformSuperAdminReconciler.ShouldApplyPassword(
            forced: true, recordedFingerprint: "fp-a", envFingerprint: "fp-a", passwordMatches: true));

    // ── Fingerprint: deterministic per credential, distinct across credentials ───
    [Fact]
    public void CredentialFingerprint_Is_Stable_For_The_Same_Credential()
        => Assert.Equal(
            PlatformSuperAdminReconciler.CredentialFingerprint("ops@opstrax.io", "Recovery2026!"),
            PlatformSuperAdminReconciler.CredentialFingerprint("ops@opstrax.io", "Recovery2026!"));

    // Email lookup is case-insensitive, so the fingerprint must not flip on casing alone —
    // otherwise every boot would read as "rotated" and reset the password.
    [Fact]
    public void CredentialFingerprint_Ignores_Email_Casing_And_Padding()
        => Assert.Equal(
            PlatformSuperAdminReconciler.CredentialFingerprint("ops@opstrax.io", "Recovery2026!"),
            PlatformSuperAdminReconciler.CredentialFingerprint("  OPS@Opstrax.IO  ", "Recovery2026!"));

    [Fact]
    public void CredentialFingerprint_Changes_When_The_Password_Rotates()
        => Assert.NotEqual(
            PlatformSuperAdminReconciler.CredentialFingerprint("ops@opstrax.io", "Recovery2026!"),
            PlatformSuperAdminReconciler.CredentialFingerprint("ops@opstrax.io", "Recovery2027!"));

    [Fact]
    public void CredentialFingerprint_Is_Salted_Per_Email()
        => Assert.NotEqual(
            PlatformSuperAdminReconciler.CredentialFingerprint("ops@opstrax.io", "Recovery2026!"),
            PlatformSuperAdminReconciler.CredentialFingerprint("other@opstrax.io", "Recovery2026!"));

    [Fact]
    public void CredentialFingerprint_Never_Contains_The_Password()
        => Assert.DoesNotContain("Recovery2026!",
            PlatformSuperAdminReconciler.CredentialFingerprint("ops@opstrax.io", "Recovery2026!"));

    [Theory]
    [InlineData(null, "fp")]
    [InlineData("fp", null)]
    [InlineData("fp", "fp-longer")]
    public void FingerprintEquals_Rejects_Null_And_Mismatched(string? a, string? b)
        => Assert.False(PlatformSuperAdminReconciler.FingerprintEquals(a, b));

    [Fact]
    public void FingerprintEquals_Accepts_Identical()
        => Assert.True(PlatformSuperAdminReconciler.FingerprintEquals("fp-a", "fp-a"));
}
