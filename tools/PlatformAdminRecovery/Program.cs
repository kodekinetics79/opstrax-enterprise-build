using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using Opstrax.Api.Services;

return await Recovery.RunAsync(args);

internal static class Recovery
{
    private const string ApplyFlag = "--apply";

    internal static async Task<int> RunAsync(string[] args)
    {
        var apply = args.Length == 2 && args[0] == ApplyFlag && !string.IsNullOrWhiteSpace(args[1]);
        if (args.Length > 0 && !apply && !(args.Length == 1 && args[0] == "--check"))
            return Fail("Usage: PlatformAdminRecovery --check | --apply <approved-change-id>");

        var email = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_EMAIL")?.Trim();
        var password = Environment.GetEnvironmentVariable("PLATFORM_SUPERADMIN_PASSWORD");
        var connectionString = Environment.GetEnvironmentVariable("PG_CONNECTION_SYSTEM");
        var expectedIdText = Environment.GetEnvironmentVariable("OPSTRAX_PLATFORM_RECOVERY_EXPECT_ADMIN_ID");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(connectionString))
            return Fail("Required recovery environment is incomplete.");
        if (!long.TryParse(expectedIdText, out var expectedId) || expectedId <= 0)
            return Fail("OPSTRAX_PLATFORM_RECOVERY_EXPECT_ADMIN_ID must be an approved positive ID.");
        if (password.Length < 12 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return Fail("The configured platform password does not meet the platform password policy.");

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var target = await LoadTargetAsync(connection, email, expectedId, null);
            if (target is null) return Fail("The approved active Platform Super Admin target was not found exactly once.");

            var alreadyMatches = VerifyPassword(password, target.PasswordHash);
            Console.WriteLine($"Preflight OK: target={target.Id}; active=true; super=true; mfaEnabled={target.MfaEnabled.ToString().ToLowerInvariant()}; activeSessions={target.ActiveSessions}; configuredPasswordMatches={alreadyMatches.ToString().ToLowerInvariant()}.");
            if (!apply)
            {
                Console.WriteLine(alreadyMatches ? "No reconciliation is required." : "Reconciliation is required; no data was changed.");
                return alreadyMatches ? 0 : 2;
            }
            if (target.MfaEnabled) return Fail("Refusing password-only recovery while MFA is enabled.");
            if (alreadyMatches) return Fail("Refusing an unnecessary recovery; the configured password already matches.");

            var changeId = args[1].Trim();
            if (changeId.Length is < 3 or > 120) return Fail("The approved change ID must be 3-120 characters.");
            var baseUrlText = Environment.GetEnvironmentVariable("OPSTRAX_PLATFORM_BASE_URL")?.Trim().TrimEnd('/');
            if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme != Uri.UriSchemeHttps)
                return Fail("OPSTRAX_PLATFORM_BASE_URL must be the production HTTPS origin.");

            var oldHash = target.PasswordHash;
            var newHash = PlatformSchemaService.HashPassword(password);
            await ApplyAsync(connection, target.Id, email, oldHash, newHash, changeId);

            try
            {
                await VerifyLoginLogoutAsync(baseUrl, email, password);
                var remainingSessions = await CountSessionsAsync(connection, target.Id);
                if (remainingSessions != 0) throw new InvalidOperationException("session_postcondition_failed");
                await WriteVerificationAuditAsync(connection, target.Id, email, changeId);
                Console.WriteLine("Recovery verified: login=ok; me=ok; logout=ok; revoked-token=401; activeSessions=0. No secret or token was printed.");
                return 0;
            }
            catch
            {
                var restored = await RollbackAsync(connection, target.Id, email, oldHash, newHash, changeId);
                Console.Error.WriteLine(restored
                    ? "Verification failed; the prior password hash was restored and all sessions were revoked."
                    : "Verification failed; automatic rollback refused because the credential changed concurrently. All sessions were revoked; escalate immediately.");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Recovery stopped safely ({ex.GetType().Name}). No credentials were printed.");
            return 1;
        }
    }

    private static async Task ApplyAsync(NpgsqlConnection connection, long id, string email, string oldHash, string newHash, string changeId)
    {
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        var locked = await LoadTargetAsync(connection, email, id, tx)
            ?? throw new InvalidOperationException("target_changed");
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(locked.PasswordHash), System.Text.Encoding.UTF8.GetBytes(oldHash)))
            throw new InvalidOperationException("credential_changed");

        await using (var update = new NpgsqlCommand(
            @"UPDATE platform_admins
              SET password_hash=@hash,invite_token_hash=NULL,invite_expires_at=NULL,updated_at=NOW()
              WHERE id=@id AND password_hash IS NOT DISTINCT FROM @old", connection, tx))
        {
            update.Parameters.AddWithValue("@hash", newHash);
            update.Parameters.AddWithValue("@old", oldHash);
            update.Parameters.AddWithValue("@id", id);
            if (await update.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("update_conflict");
        }
        var revoked = await DeleteSessionsAsync(connection, tx, id);
        await InsertAuditAsync(connection, tx, id, email, "platform.admin.break_glass_password_reconciled", changeId,
            JsonSerializer.Serialize(new { changeId, sessionsRevoked = revoked, source = "render_environment", configuredSecretReadFromEnvironment = true }));
        await tx.CommitAsync();
    }

    private static async Task<bool> RollbackAsync(NpgsqlConnection connection, long id, string email, string oldHash, string expectedNewHash, string changeId)
    {
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        string? current;
        await using (var read = new NpgsqlCommand("SELECT password_hash FROM platform_admins WHERE id=@id FOR UPDATE", connection, tx))
        {
            read.Parameters.AddWithValue("@id", id);
            current = (await read.ExecuteScalarAsync())?.ToString();
        }
        var canRestore = current is not null && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(current), System.Text.Encoding.UTF8.GetBytes(expectedNewHash));
        if (canRestore)
        {
            await using var restore = new NpgsqlCommand("UPDATE platform_admins SET password_hash=@old,updated_at=NOW() WHERE id=@id", connection, tx);
            restore.Parameters.AddWithValue("@old", oldHash);
            restore.Parameters.AddWithValue("@id", id);
            await restore.ExecuteNonQueryAsync();
        }
        var revoked = await DeleteSessionsAsync(connection, tx, id);
        await InsertAuditAsync(connection, tx, id, email, "platform.admin.break_glass_password_rollback", changeId,
            JsonSerializer.Serialize(new { changeId, priorHashRestored = canRestore, sessionsRevoked = revoked, reason = "verification_failed" }));
        await tx.CommitAsync();
        return canRestore;
    }

    private static async Task VerifyLoginLogoutAsync(Uri baseUrl, string email, string password)
    {
        using var client = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(20) };
        using var login = await client.PostAsJsonAsync("/api/platform/auth/login", new { email, password });
        if (login.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("login_failed");
        using var document = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("token", out var tokenElement) || string.IsNullOrWhiteSpace(tokenElement.GetString()))
            throw new InvalidOperationException("login_contract_failed");
        var token = tokenElement.GetString()!;
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var me = await client.GetAsync("/api/platform/auth/me");
        if (me.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("me_failed");
        using var logout = await client.PostAsync("/api/platform/auth/logout", null);
        if (logout.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("logout_failed");
        using var rejected = await client.GetAsync("/api/platform/auth/me");
        if (rejected.StatusCode != HttpStatusCode.Unauthorized) throw new InvalidOperationException("logout_token_still_active");
    }

    private static async Task<Target?> LoadTargetAsync(NpgsqlConnection connection, string email, long expectedId, NpgsqlTransaction? tx)
    {
        await using var command = new NpgsqlCommand(
            @"SELECT a.id,a.password_hash,a.mfa_enabled,
                     (SELECT COUNT(*) FROM platform_sessions s WHERE s.admin_id=a.id AND s.expires_at>NOW()) active_sessions
              FROM platform_admins a JOIN platform_roles r ON r.id=a.role_id
              WHERE LOWER(a.email)=LOWER(@email) AND a.id=@id AND a.status='Active' AND r.role_key='platform_super_admin'" +
            (tx is null ? "" : " FOR UPDATE OF a"), connection, tx);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@id", expectedId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        var target = new Target(reader.GetInt64(0), reader.GetString(1), reader.GetBoolean(2), reader.GetInt64(3));
        if (await reader.ReadAsync()) return null;
        return target;
    }

    private static async Task<long> DeleteSessionsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, long id)
    {
        await using var command = new NpgsqlCommand(
            "WITH deleted AS (DELETE FROM platform_sessions WHERE admin_id=@id RETURNING 1) SELECT COUNT(*) FROM deleted", connection, tx);
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountSessionsAsync(NpgsqlConnection connection, long id)
    {
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM platform_sessions WHERE admin_id=@id", connection);
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task InsertAuditAsync(NpgsqlConnection connection, NpgsqlTransaction tx, long id, string email, string action, string changeId, string details)
    {
        await using var command = new NpgsqlCommand(
            @"INSERT INTO platform_audit_log
                (actor_admin_id,actor_email,actor_role,action,entity_type,entity_id,details_json,ip_address)
              VALUES (NULL,@email,'break_glass',@action,'PlatformAdmin',@id,@details::jsonb,'render-one-off')", connection, tx);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@details", details);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WriteVerificationAuditAsync(NpgsqlConnection connection, long id, string email, string changeId)
    {
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await InsertAuditAsync(connection, tx, id, email, "platform.admin.break_glass_recovery_verified", changeId,
            JsonSerializer.Serialize(new { changeId, login = true, me = true, logout = true, revokedTokenRejected = true, activeSessions = 0 }));
        await tx.CommitAsync();
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != 16 || expected.Length != 32 || iterations is < 100_000 or > 2_000_000) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch { return false; }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private sealed record Target(long Id, string PasswordHash, bool MfaEnabled, long ActiveSessions);
}
