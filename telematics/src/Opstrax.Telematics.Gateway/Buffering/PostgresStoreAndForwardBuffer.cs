using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Telematics.Contracts;
using Opstrax.Telematics.Contracts.Eventing;

namespace Opstrax.Telematics.Gateway.Buffering;

/// <summary>
/// Durable system-ledger store-and-forward queue. Rows survive process restarts and claims are
/// leases: an event is deleted only after broker publication succeeds, while a claim abandoned by
/// a crashed gateway becomes eligible again after the bounded lease timeout.
/// </summary>
internal sealed class PostgresStoreAndForwardBuffer : IStoreAndForwardBuffer, IDisposable
{
    private static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(5);
    private readonly string _connectionString;
    private readonly byte[] _encryptionKey;
    private int _approximateCount;

    public PostgresStoreAndForwardBuffer(string systemConnectionString, byte[] encryptionKey)
    {
        if (string.IsNullOrWhiteSpace(systemConnectionString))
            throw new ArgumentException("A telematics system connection string is required.", nameof(systemConnectionString));
        if (encryptionKey is not { Length: 32 })
            throw new ArgumentException("The store-forward encryption key must be 32 bytes.", nameof(encryptionKey));
        _connectionString = systemConnectionString;
        _encryptionKey = encryptionKey.ToArray();
    }

    public int Count => Math.Max(0, Volatile.Read(ref _approximateCount));

    public async ValueTask EnqueueAsync(StoreAndForwardEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.Envelope is not EventEnvelope<CanonicalTelemetryEvent> envelope)
            throw new InvalidOperationException("The production store-forward ledger accepts canonical telemetry envelopes only.");

        string protectedJson = Protect(envelope);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = """
            INSERT INTO telemetry_store_forward
                (event_id, topic, partition_key, envelope_json, enqueued_at, attempts)
            VALUES
                (@event_id, @topic, @partition_key, @envelope_json::jsonb, @enqueued_at, 0)
            ON CONFLICT (event_id) DO NOTHING;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("event_id", envelope.EventId);
        command.Parameters.AddWithValue("topic", entry.Topic);
        command.Parameters.AddWithValue("partition_key", entry.Key);
        command.Parameters.Add(new NpgsqlParameter("envelope_json", NpgsqlDbType.Text) { Value = protectedJson });
        command.Parameters.AddWithValue("enqueued_at", entry.EnqueuedAt);
        int inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (inserted == 1) Interlocked.Increment(ref _approximateCount);
    }

    public async ValueTask<StoreAndForwardLease?> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        var token = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        const string sql = """
            UPDATE telemetry_store_forward q
               SET claim_token=@claim_token, claimed_at=NOW(), attempts=q.attempts+1
             WHERE q.id=(
                   SELECT candidate.id
                     FROM telemetry_store_forward candidate
                    WHERE (candidate.claim_token IS NULL
                       OR candidate.claimed_at IS NULL
                       OR candidate.claimed_at < NOW() - @claim_timeout)
                      AND NOT EXISTS (
                          SELECT 1 FROM telemetry_store_forward earlier
                           WHERE earlier.partition_key=candidate.partition_key
                             AND earlier.id<candidate.id)
                    ORDER BY candidate.id
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1)
            RETURNING q.topic, q.partition_key, q.envelope_json::text, q.enqueued_at;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("claim_token", token);
        command.Parameters.AddWithValue("claim_timeout", ClaimTimeout);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        var topic = reader.GetString(0);
        var key = reader.GetString(1);
        var envelope = Unprotect(reader.GetString(2));
        var enqueuedAt = reader.GetFieldValue<DateTimeOffset>(3);
        return new StoreAndForwardLease(token, new StoreAndForwardEntry(topic, key, envelope, enqueuedAt));
    }

    public async ValueTask CompleteAsync(StoreAndForwardLease lease, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "DELETE FROM telemetry_store_forward WHERE claim_token=@claim_token", connection);
        command.Parameters.AddWithValue("claim_token", lease.Token);
        int deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (deleted == 1) Interlocked.Decrement(ref _approximateCount);
    }

    public async ValueTask AbandonAsync(
        StoreAndForwardLease lease,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            UPDATE telemetry_store_forward
               SET claim_token=NULL, claimed_at=NULL, last_error=@last_error
             WHERE claim_token=@claim_token
            """, connection);
        command.Parameters.AddWithValue("claim_token", lease.Token);
        command.Parameters.AddWithValue("last_error", (object?)Truncate(reason, 500) ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private string Protect(EventEnvelope<CanonicalTelemetryEvent> envelope)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(envelope);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[plaintext.Length];
        byte[] tag = new byte[16];
        try
        {
            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Encrypt(nonce, plaintext, cipher, tag);
            return JsonSerializer.Serialize(new ProtectedEnvelope(
                "aes-256-gcm-v1",
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(tag),
                Convert.ToBase64String(cipher)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private EventEnvelope<CanonicalTelemetryEvent> Unprotect(string protectedJson)
    {
        ProtectedEnvelope? protectedEnvelope = JsonSerializer.Deserialize<ProtectedEnvelope>(protectedJson);
        if (protectedEnvelope is null || protectedEnvelope.Format != "aes-256-gcm-v1")
            throw new CryptographicException("Unsupported store-forward envelope format.");

        byte[] nonce = Convert.FromBase64String(protectedEnvelope.Nonce);
        byte[] tag = Convert.FromBase64String(protectedEnvelope.Tag);
        byte[] cipher = Convert.FromBase64String(protectedEnvelope.Ciphertext);
        byte[] plaintext = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Decrypt(nonce, cipher, tag, plaintext);
            return JsonSerializer.Deserialize<EventEnvelope<CanonicalTelemetryEvent>>(plaintext)
                ?? throw new JsonException("Store-forward envelope payload is empty.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];

    public void Dispose() => CryptographicOperations.ZeroMemory(_encryptionKey);

    private sealed record ProtectedEnvelope(string Format, string Nonce, string Tag, string Ciphertext);
}
