using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// PasswordPolicyService — Scoped
//
// Validates passwords against tenant policy and manages login lockout state.
//
// SECURITY PRINCIPLES:
//   - Login errors are always generic ("Invalid credentials") — never reveal
//     whether the email exists, whether the account is locked, or the reason
//     for failure. This prevents user enumeration.
//   - Lockout state check is done BEFORE password comparison — fail fast on
//     locked accounts without hitting bcrypt.
//   - Failed attempts are incremented on wrong password only (not lockout hit).
//   - Lockout is lifted automatically after lockout_duration_minutes.
//   - All events are logged to security_events.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class PasswordPolicyService(Database db, SecurityEventService secEvent)
{
    // Pure static — no DB, no IO. Validates a password against the policy rules.
    public static (bool valid, string[] failures) ValidatePassword(
        string password, SecuritySettings settings)
    {
        var failures = new List<string>();

        if (password.Length < settings.PasswordMinLength)
            failures.Add($"Minimum {settings.PasswordMinLength} characters required");

        if (settings.PasswordRequiresUppercase && !password.Any(char.IsUpper))
            failures.Add("Must contain at least one uppercase letter");

        if (settings.PasswordRequiresNumber && !password.Any(char.IsDigit))
            failures.Add("Must contain at least one number");

        if (settings.PasswordRequiresSymbol && !password.Any(c => !char.IsLetterOrDigit(c)))
            failures.Add("Must contain at least one special character");

        return (failures.Count == 0, [.. failures]);
    }

    // Check whether an account is currently locked. Returns (isLocked, lockedUntil).
    // ALWAYS called before password comparison.
    public async Task<(bool isLocked, DateTime? lockedUntil)> CheckLockoutAsync(
        long userId, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            "SELECT locked_until FROM users WHERE id = @uid LIMIT 1",
            c => c.Parameters.AddWithValue("@uid", userId), ct);

        if (row is null) return (false, null);

        if (row["lockedUntil"] is DateTime until && until > DateTime.UtcNow)
            return (true, until);

        return (false, null);
    }

    // Increment failed login count. Lock account if threshold reached.
    public async Task RecordFailedLoginAsync(
        long companyId, long userId,
        SecuritySettings settings,
        string? sourceIp, string? userAgent,
        CancellationToken ct = default)
    {
        var newCount = await db.ScalarLongAsync(
            @"UPDATE users
              SET failed_login_attempts = failed_login_attempts + 1
              WHERE id = @uid;
              SELECT failed_login_attempts FROM users WHERE id = @uid LIMIT 1",
            c => c.Parameters.AddWithValue("@uid", userId), ct);

        await secEvent.LogAsync(companyId, userId, "login.failure", "medium",
            sourceIp, userAgent, false,
            $"Login failed — attempt {newCount} of {settings.MaxFailedLoginAttempts}",
            new { attempt = newCount, maxAttempts = settings.MaxFailedLoginAttempts }, ct);

        if (newCount >= settings.MaxFailedLoginAttempts)
        {
            var lockUntil = DateTime.UtcNow.AddMinutes(settings.LockoutDurationMinutes);
            await db.ExecuteAsync(
                "UPDATE users SET locked_until = @until WHERE id = @uid",
                c =>
                {
                    c.Parameters.AddWithValue("@until", lockUntil);
                    c.Parameters.AddWithValue("@uid",   userId);
                }, ct);

            await secEvent.LogAsync(companyId, userId, "account.locked", "high",
                sourceIp, userAgent, false,
                $"Account locked until {lockUntil:yyyy-MM-ddTHH:mmZ} after {newCount} failed attempts",
                new { lockUntil, failedAttempts = newCount }, ct);
        }
    }

    // Reset failed attempt count on successful login.
    public async Task RecordSuccessfulLoginAsync(
        long companyId, long userId,
        string? sourceIp, string? userAgent,
        CancellationToken ct = default)
    {
        await db.ExecuteAsync(
            "UPDATE users SET failed_login_attempts = 0, locked_until = NULL WHERE id = @uid",
            c => c.Parameters.AddWithValue("@uid", userId), ct);

        await secEvent.LogAsync(companyId, userId, "login.success", "info",
            sourceIp, userAgent, true,
            "Login succeeded", null, ct);
    }

    // Check if password has expired (returns true = must change).
    public static bool IsPasswordExpired(DateTime? passwordChangedAt, SecuritySettings settings)
    {
        if (settings.PasswordExpiryDays <= 0) return false;
        if (passwordChangedAt is null) return true;
        return (DateTime.UtcNow - passwordChangedAt.Value).TotalDays > settings.PasswordExpiryDays;
    }
}
