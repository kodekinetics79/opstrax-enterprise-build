using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Data;
using Xunit.Abstractions;

namespace Opstrax.Tests;

public sealed class TenantTicketLoadPostgresTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SignedTenantBootstrap_SurvivesPoolSaturation_WithinPilotLatencyBudget()
    {
        const int operations = 400;
        const int concurrency = 24;
        var owner = Database(TestDb.ConnectionString, TestDb.ConnectionString, false, 2);
        var runtime = Database(TestDb.AppConnectionString, TestDb.SystemConnectionString, true, 8);
        var suffix = Guid.NewGuid().ToString("N");
        var tenantA = 981_000_000L + Random.Shared.Next(1, 400_000);
        var tenantB = 982_000_000L + Random.Shared.Next(1, 400_000);
        var markerA = $"TKT-LOAD-A-{suffix}";
        var markerB = $"TKT-LOAD-B-{suffix}";
        await owner.ExecuteAsync(
            @"INSERT INTO dvir_reports(company_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status)
              VALUES (@a,@ma,0,0,'Pre-Trip','Submitted'),(@b,@mb,0,0,'Pre-Trip','Submitted')",
            c =>
            {
                c.Parameters.AddWithValue("@a", tenantA); c.Parameters.AddWithValue("@ma", markerA);
                c.Parameters.AddWithValue("@b", tenantB); c.Parameters.AddWithValue("@mb", markerB);
            });

        try
        {
            var xidBefore = await owner.ScalarLongAsync("SELECT txid_current()::bigint");
            var frozenAgeBefore = await owner.ScalarLongAsync(
                "SELECT age(datfrozenxid)::bigint FROM pg_database WHERE datname=current_database()");
            var latencies = new ConcurrentBag<double>();
            var failures = new ConcurrentQueue<string>();
            var total = Stopwatch.StartNew();

            await Parallel.ForEachAsync(Enumerable.Range(0, operations),
                new ParallelOptions { MaxDegreeOfParallelism = concurrency },
                async (i, ct) =>
                {
                    var tenant = i % 2 == 0 ? tenantA : tenantB;
                    var ownMarker = i % 2 == 0 ? markerA : markerB;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var rows = await runtime.RunInTenantScopeAsync(tenant, () => runtime.QueryAsync(
                            "SELECT report_number FROM dvir_reports WHERE report_number=ANY(@markers)",
                            c => c.Parameters.AddWithValue("@markers", new[] { markerA, markerB }), ct), ct);
                        if (rows.Count != 1 || !string.Equals(rows[0]["reportNumber"]?.ToString(), ownMarker,
                                StringComparison.Ordinal))
                            failures.Enqueue($"tenant isolation mismatch at operation {i}");
                    }
                    catch (Exception ex) { failures.Enqueue($"{ex.GetType().Name} at operation {i}"); }
                    finally { latencies.Add(sw.Elapsed.TotalMilliseconds); }
                });
            total.Stop();

            var xidAfter = await owner.ScalarLongAsync("SELECT txid_current()::bigint");
            var frozenAgeAfter = await owner.ScalarLongAsync(
                "SELECT age(datfrozenxid)::bigint FROM pg_database WHERE datname=current_database()");
            var sorted = latencies.OrderBy(x => x).ToArray();
            var p95 = Percentile(sorted, 0.95);
            var p99 = Percentile(sorted, 0.99);
            var rps = operations / total.Elapsed.TotalSeconds;
            var xidDelta = xidAfter - xidBefore;
            output.WriteLine(
                $"operations={operations}; concurrency={concurrency}; app_pool=8; system_pool=8; " +
                $"rps={rps:F1}; p95_ms={p95:F1}; p99_ms={p99:F1}; xid_delta={xidDelta}; " +
                $"datfrozen_age_before={frozenAgeBefore}; datfrozen_age_after={frozenAgeAfter}");

            Assert.Empty(failures);
            Assert.Equal(operations, sorted.Length);
            Assert.True(rps >= 40, $"Signed-scope throughput below pilot floor: {rps:F1} req/s");
            Assert.True(p95 <= 500, $"Signed-scope p95 exceeds API SLO: {p95:F1} ms");
            Assert.InRange(xidDelta, operations, operations + 32);
        }
        finally
        {
            await owner.ExecuteAsync("DELETE FROM dvir_reports WHERE report_number=ANY(@markers)",
                c => c.Parameters.AddWithValue("@markers", new[] { markerA, markerB }));
            NpgsqlConnection.ClearAllPools();
        }
    }

    private static double Percentile(double[] sorted, double percentile) =>
        sorted.Length == 0 ? double.PositiveInfinity : sorted[(int)Math.Ceiling(sorted.Length * percentile) - 1];

    private static Database Database(
        string appConnection, string systemConnection, bool enforceRls, int maxPool)
    {
        static string WithPool(string source, int max) => new NpgsqlConnectionStringBuilder(source)
        {
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = max,
        }.ConnectionString;
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = WithPool(appConnection, maxPool),
            ["ConnectionStrings:SystemConnection"] = WithPool(systemConnection, maxPool),
            ["Rls:EnforceTenantContext"] = enforceRls.ToString(),
            ["Rls:TenantTicketTtlSeconds"] = "120",
        }).Build();
        return new Database(config, new TenantScopeAccessor());
    }
}
