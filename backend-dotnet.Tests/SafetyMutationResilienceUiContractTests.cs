namespace Opstrax.Tests;

public sealed class SafetyMutationResilienceUiContractTests
{
    [Fact]
    public void CriticalSafetyMutationsHaveSynchronousSingleFlightAdmission()
    {
        var guard = Read("frontend", "src", "hooks", "useSingleFlight.ts");
        Assert.Contains("if (active.current) return false", guard, StringComparison.Ordinal);
        Assert.Contains("active.current = true", guard, StringComparison.Ordinal);
        Assert.Contains("finally", guard, StringComparison.Ordinal);
        Assert.Contains("active.current = false", guard, StringComparison.Ordinal);

        var safety = Read("frontend", "src", "pages", "Batch4SafetyPage.tsx");
        Assert.Contains("saveSingleFlight(() => save.mutateAsync(payload))", safety, StringComparison.Ordinal);
        Assert.Contains("actionSingleFlight(() => action.mutateAsync", safety, StringComparison.Ordinal);

        var dvir = Read("frontend", "src", "pages", "DvirInspectionsPage.tsx");
        Assert.Contains("actionSingleFlight(() => action.mutateAsync", dvir, StringComparison.Ordinal);
        Assert.Contains("createSingleFlight(() => mutation.mutateAsync())", dvir, StringComparison.Ordinal);

        var hosEld = Read("frontend", "src", "pages", "HosEldPage.tsx");
        Assert.Contains("markSingleFlight(() => markMalfMut.mutateAsync", hosEld, StringComparison.Ordinal);
        Assert.Contains("resolveSingleFlight(() => resolveMalfMut.mutateAsync", hosEld, StringComparison.Ordinal);

        var driverCoaching = Read("frontend", "src", "pages", "driver", "DriverCoachingPage.tsx");
        Assert.Contains("acknowledgeSingleFlight(() => ackMut.mutateAsync", driverCoaching, StringComparison.Ordinal);
        var driverHos = Read("frontend", "src", "pages", "driver", "DriverHosPage.tsx");
        Assert.Contains("certifySingleFlight(() => certify.mutateAsync", driverHos, StringComparison.Ordinal);
        var driverDvir = Read("frontend", "src", "pages", "driver", "DriverDvirPage.tsx");
        Assert.Contains("submitSingleFlight(() => submitMut.mutateAsync", driverDvir, StringComparison.Ordinal);
        Assert.Contains("if (signRequest.current) return signRequest.current", driverDvir, StringComparison.Ordinal);
        Assert.Contains("if (acknowledgeRequest.current) return acknowledgeRequest.current", driverDvir, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderAndReadFailuresNeverCollapseIntoHealthyOrEmptySafetyState()
    {
        var driverDvir = Read("frontend", "src", "pages", "driver", "DriverDvirPage.tsx");
        Assert.Contains("No pending state has been inferred", driverDvir, StringComparison.Ordinal);
        Assert.Contains("No acknowledgment state has been inferred", driverDvir, StringComparison.Ordinal);
        Assert.Contains("No unassigned state has been inferred", driverDvir, StringComparison.Ordinal);
        Assert.Contains("Retry certifications", driverDvir, StringComparison.Ordinal);

        var driverHos = Read("frontend", "src", "pages", "driver", "DriverHosPage.tsx");
        Assert.Contains("No certification state has been inferred", driverHos, StringComparison.Ordinal);
        Assert.Contains("Retry daily records", driverHos, StringComparison.Ordinal);

        var scorecards = Read("frontend", "src", "pages", "DriverScorecardsPage.tsx");
        Assert.Contains("onRetry={() => { void driversQ.refetch(); void summaryQ.refetch(); }}", scorecards, StringComparison.Ordinal);
        Assert.Contains("onRetry={() => void vehiclesQ.refetch()}", scorecards, StringComparison.Ordinal);
        Assert.Contains("onRetry={() => void trendsQ.refetch()}", scorecards, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "backend-dotnet")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()));
    }
}
