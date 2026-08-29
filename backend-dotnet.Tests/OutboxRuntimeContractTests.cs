namespace Opstrax.Tests;

public sealed class OutboxRuntimeContractTests
{
    [Fact]
    public void ProductionManifest_LeavesDispatcherBlocked_WhileLocalRehearsalEnablesIt()
    {
        var render = Read("render.yaml");
        var compose = Read("docker-compose.yml");

        Assert.DoesNotContain("key: OutboxDispatcher__Enabled", render, StringComparison.Ordinal);
        Assert.DoesNotContain("key: OutboxDispatcher__AllowProduction", render, StringComparison.Ordinal);
        Assert.Contains("OutboxDispatcher__Enabled: \"true\"", compose, StringComparison.Ordinal);
        Assert.Contains("OutboxDispatcher__AllowProduction: \"true\"", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatcher_IsTrackedOnlyWhenItIsActuallyRegistered()
    {
        var worker = Read("backend-dotnet", "Services", "OutboxDispatcherBackgroundService.cs");
        var readiness = Read("backend-dotnet", "Services", "FleetProductionReadinessService.cs");

        Assert.Contains("ServiceRunTracker tracker", worker, StringComparison.Ordinal);
        Assert.Contains("tracker.BeginAsync(ServiceName", worker, StringComparison.Ordinal);
        Assert.Contains("tracker.CompleteAsync(runId, ServiceName", worker, StringComparison.Ordinal);
        Assert.Contains("tracker.FailAsync(runId, ServiceName", worker, StringComparison.Ordinal);
        Assert.Contains("outboxOptions.Enabled", readiness, StringComparison.Ordinal);
        Assert.Contains("outboxOptions.AllowProduction", readiness, StringComparison.Ordinal);
        Assert.Contains("\"OutboxDispatcherBackgroundService\"", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatcher_RecoversExpiredOutboxAndInboxClaims()
    {
        var dispatcher = Read("backend-dotnet", "Foundation", "FoundationDispatcherServices.cs");

        Assert.Contains("RecoverExpiredOutboxClaimsAsync", dispatcher, StringComparison.Ordinal);
        Assert.Contains("RecoverExpiredInboxClaimsAsync", dispatcher, StringComparison.Ordinal);
        Assert.True(dispatcher.Split("Processing lease expired before completion", StringSplitOptions.None).Length - 1 >= 4);
        Assert.True(dispatcher.Split("locked_until < NOW()", StringSplitOptions.None).Length - 1 >= 4);
        Assert.True(dispatcher.Split("next_attempt_at IS NULL OR next_attempt_at <= NOW()", StringSplitOptions.None).Length - 1 >= 2);
        Assert.True(dispatcher.Split("THEN 'dead_letter' ELSE 'retry_pending'", StringSplitOptions.None).Length - 1 >= 2);
    }

    [Fact]
    public void ReliabilitySnapshot_ExposesOnlyAggregateOutboxFailureCounts()
    {
        var reliability = Read("backend-dotnet", "Observability", "ReliabilityService.cs");
        var start = reliability.IndexOf("private async Task<ComponentHealth> IntegrationsHealthAsync", StringComparison.Ordinal);
        var end = reliability.IndexOf("// ── Per-tenant reliability", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var outboxHealth = reliability[start..end];

        Assert.Contains("status='retry_pending'", outboxHealth, StringComparison.Ordinal);
        Assert.Contains("status='dead_letter'", outboxHealth, StringComparison.Ordinal);
        Assert.Contains("AS stranded", outboxHealth, StringComparison.Ordinal);
        Assert.Contains("[\"retryDue\"]", outboxHealth, StringComparison.Ordinal);
        Assert.DoesNotContain("payload_json", outboxHealth, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("last_error", outboxHealth, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dead_letter_reason", outboxHealth, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepoRoot(), .. segments]));

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
}
