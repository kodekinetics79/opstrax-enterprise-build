using Opstrax.Api.Data;

namespace Opstrax.Api.Services.Connectors;

public sealed record ConnectorOperationContext(
    long CompanyId,
    long IntegrationId,
    long Generation,
    Guid LeaseToken,
    string IntegrationKey,
    object? ConfigJson,
    string Status,
    bool IsSyncOperation);

public sealed class StaleConnectorOperationException(string message) : InvalidOperationException(message);

// All provider I/O runs outside a database transaction.  This short committed lease
// is the durable bridge between that I/O and later writes.  Configure/disconnect bump
// operation_generation and clear the token, so a result captured before either action
// is provably stale and cannot mutate connector or telemetry state.
public static class ConnectorOperationLease
{
    public static async Task<ConnectorOperationContext?> TryAcquireAsync(
        Database db,
        long companyId,
        long integrationId,
        string[] allowedStatuses,
        TimeSpan duration,
        CancellationToken ct,
        bool isSyncOperation = false)
    {
        var token = Guid.NewGuid();
        var leaseSeconds = Math.Clamp((int)Math.Ceiling(duration.TotalSeconds), 10, 180);
        return await db.RunInSystemTransactionAsync(async () =>
        {
            var row = await db.QuerySingleAsync(
                @"UPDATE integrations SET
                      operation_lease_token=@token,
                      operation_lease_expires_at=NOW() + (@leaseSeconds * INTERVAL '1 second'),
                      operation_last_attempt_at=NOW(),
                      sync_last_attempt_at=CASE WHEN @isSyncOperation THEN NOW() ELSE sync_last_attempt_at END,
                      updated_at=NOW()
                  WHERE company_id=@cid AND id=@id
                    AND status = ANY(@statuses)
                    AND (operation_lease_token IS NULL OR operation_lease_expires_at <= NOW())
                  RETURNING company_id,id,operation_generation,operation_lease_token,
                            integration_key,config_json,status",
                c =>
                {
                    c.Parameters.AddWithValue("@token", token);
                    c.Parameters.AddWithValue("@leaseSeconds", leaseSeconds);
                    c.Parameters.AddWithValue("@cid", companyId);
                    c.Parameters.AddWithValue("@id", integrationId);
                    c.Parameters.AddWithValue("@statuses", allowedStatuses);
                    c.Parameters.AddWithValue("@isSyncOperation", isSyncOperation);
                }, ct);
            if (row is null) return null;
            return new ConnectorOperationContext(
                Convert.ToInt64(row["companyId"]),
                Convert.ToInt64(row["id"]),
                Convert.ToInt64(row["operationGeneration"]),
                (Guid)row["operationLeaseToken"]!,
                row["integrationKey"]?.ToString() ?? "",
                row.GetValueOrDefault("configJson"),
                row["status"]?.ToString() ?? "",
                isSyncOperation);
        }, ct);
    }

    public static async Task AssertCurrentForWriteAsync(
        Database db,
        ConnectorOperationContext operation,
        CancellationToken ct)
    {
        var row = await db.QuerySingleAsync(
            @"SELECT id FROM integrations
              WHERE company_id=@cid AND id=@id
                AND operation_generation=@generation
                AND operation_lease_token=@token
                AND operation_lease_expires_at > NOW()
                AND status IN ('Connected','Error')
              FOR UPDATE",
            c => Bind(c, operation), ct);
        if (row is null)
            throw new StaleConnectorOperationException(
                "The connector operation was invalidated before provider data could be committed.");
    }

    public static Task<int> CompleteTestAsync(
        Database db,
        ConnectorOperationContext operation,
        ConnectorResult result,
        CancellationToken ct) => db.RunInSystemTransactionAsync(async () =>
            await db.ExecuteAsync(
                @"UPDATE integrations SET
                      status=CASE WHEN @ok THEN 'Connected' ELSE 'Error' END,
                      last_tested_at=NOW(),last_test_ok=@ok,last_test_message=@message,
                      operation_lease_token=NULL,operation_lease_expires_at=NULL,updated_at=NOW()
                  WHERE company_id=@cid AND id=@id
                    AND operation_generation=@generation
                    AND operation_lease_token=@token
                    AND operation_lease_expires_at > NOW()",
                c =>
                {
                    Bind(c, operation);
                    c.Parameters.AddWithValue("@ok", result.Success);
                    c.Parameters.AddWithValue("@message", (object?)result.Message ?? DBNull.Value);
                }, ct), ct);

    public static Task<int> CompleteSyncAsync(
        Database db,
        ConnectorOperationContext operation,
        ConnectorResult result,
        string? nextCursor,
        CancellationToken ct) => db.RunInSystemTransactionAsync(async () =>
            await db.ExecuteAsync(
                @"UPDATE integrations SET
                      status=CASE WHEN @ok THEN 'Connected' ELSE 'Error' END,
                      last_sync_at=CASE WHEN @ok THEN NOW() ELSE last_sync_at END,
                      sync_label=CASE WHEN @ok THEN 'Just now' ELSE sync_label END,
                      sync_last_completed_at=NOW(),sync_last_ok=@ok,
                      config_json=CASE WHEN @cursor IS NULL THEN config_json
                                       ELSE COALESCE(config_json,'{}'::jsonb) || jsonb_build_object('syncCursor',@cursor::text) END,
                      operation_lease_token=NULL,operation_lease_expires_at=NULL,updated_at=NOW()
                  WHERE company_id=@cid AND id=@id
                    AND operation_generation=@generation
                    AND operation_lease_token=@token
                    AND operation_lease_expires_at > NOW()",
                c =>
                {
                    Bind(c, operation);
                    c.Parameters.AddWithValue("@ok", result.Success);
                    c.Parameters.AddWithValue("@cursor", (object?)nextCursor ?? DBNull.Value);
                }, ct), ct);

    public static Task<int> ReleaseAsErrorAsync(
        Database db,
        ConnectorOperationContext operation,
        CancellationToken ct) => db.RunInSystemTransactionAsync(async () =>
            await db.ExecuteAsync(
                @"UPDATE integrations SET status='Error',
                      sync_last_completed_at=CASE WHEN @isSyncOperation THEN NOW() ELSE sync_last_completed_at END,
                      sync_last_ok=CASE WHEN @isSyncOperation THEN false ELSE sync_last_ok END,
                      operation_lease_token=NULL,operation_lease_expires_at=NULL,updated_at=NOW()
                  WHERE company_id=@cid AND id=@id
                    AND operation_generation=@generation
                    AND operation_lease_token=@token",
                c =>
                {
                    Bind(c, operation);
                    c.Parameters.AddWithValue("@isSyncOperation", operation.IsSyncOperation);
                }, ct), ct);

    private static void Bind(Npgsql.NpgsqlCommand command, ConnectorOperationContext operation)
    {
        command.Parameters.AddWithValue("@cid", operation.CompanyId);
        command.Parameters.AddWithValue("@id", operation.IntegrationId);
        command.Parameters.AddWithValue("@generation", operation.Generation);
        command.Parameters.AddWithValue("@token", operation.LeaseToken);
    }
}
