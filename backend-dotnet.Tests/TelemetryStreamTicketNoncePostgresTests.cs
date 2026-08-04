using Microsoft.Extensions.Configuration;
using Opstrax.Api;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class TelemetryStreamTicketNoncePostgresTests
{
    [Fact]
    public async Task SameNonce_AcrossInstancesAndConcurrentConsumers_IsConsumedExactlyOnce()
    {
        var issuer = CreateDatabase();
        await EnsureTableAsync(issuer);
        var nonce = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var hash = TelemetryTicketHelper.HashNonce(nonce);
        const long userId = 700001;
        const long companyId = 800001;
        const long branchId = 900001;

        try
        {
            await issuer.ExecuteAsync(
                @"INSERT INTO telemetry_stream_ticket_nonces
                    (nonce_hash,audit_company_id,branch_id,user_id,expires_at)
                  VALUES (@n,@c,@b,@u,NOW()+INTERVAL '90 seconds')",
                c => { c.Parameters.AddWithValue("@n", hash); c.Parameters.AddWithValue("@c", companyId);
                       c.Parameters.AddWithValue("@b", branchId); c.Parameters.AddWithValue("@u", userId); });

            // Independent Database instances model separate app replicas/restarts.
            var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                ConsumeAsync(CreateDatabase(), hash, userId, companyId, branchId)));
            Assert.Equal(1, attempts.Count(rows => rows == 1));
            Assert.Equal(7, attempts.Count(rows => rows == 0));
            Assert.Equal(0, await ConsumeAsync(CreateDatabase(), hash, userId, companyId, branchId));
        }
        finally
        {
            await issuer.ExecuteAsync("DELETE FROM telemetry_stream_ticket_nonces WHERE nonce_hash=@n",
                c => c.Parameters.AddWithValue("@n", hash));
        }
    }

    private static Task<int> ConsumeAsync(Database db, string hash, long userId, long companyId, long? branchId) =>
        db.ExecuteAsync(
            @"UPDATE telemetry_stream_ticket_nonces SET consumed_at=NOW()
              WHERE nonce_hash=@n AND user_id=@u AND audit_company_id=@c
                AND branch_id IS NOT DISTINCT FROM @b
                AND consumed_at IS NULL AND expires_at>NOW()",
            c => { c.Parameters.AddWithValue("@n", hash); c.Parameters.AddWithValue("@u", userId);
                   c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@b", (object?)branchId ?? DBNull.Value); });

    private static Database CreateDatabase() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString,
        }).Build());

    private static Task<int> EnsureTableAsync(Database db) => db.ExecuteAsync(
        @"CREATE TABLE IF NOT EXISTS telemetry_stream_ticket_nonces (
            nonce_hash VARCHAR(64) PRIMARY KEY,
            audit_company_id BIGINT NOT NULL,
            branch_id BIGINT NULL,
            user_id BIGINT NOT NULL,
            expires_at TIMESTAMPTZ NOT NULL,
            consumed_at TIMESTAMPTZ NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())");
}
