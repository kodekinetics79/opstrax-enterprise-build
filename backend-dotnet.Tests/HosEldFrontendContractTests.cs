using System;
using System.IO;
using Xunit;

namespace Opstrax.Tests;

public sealed class HosEldFrontendContractTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

    [Fact]
    public void RenderedHosPage_UsesTheApisCamelCaseJsonContract()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "HosEldPage.tsx"));

        foreach (var expected in new[]
        {
            "d.driverName", "d.driverCode", "d.driveTimeRemainingMinutes",
            "d.shiftTimeRemainingMinutes", "d.cycleTimeRemainingMinutes", "d.hosWarning",
            "l.logDate", "l.startTime", "l.calculatedDurationMinutes", "l.isCertified",
            "e.deviceSerial", "e.vehicleCode", "e.lastSyncAt", "rec.actionLabel",
        })
            Assert.Contains(expected, page, StringComparison.Ordinal);

        foreach (var staleSnakeCase in new[]
        {
            ".driver_name", ".driver_code", ".drive_time_remaining_minutes",
            ".shift_time_remaining_minutes", ".cycle_time_remaining_minutes", ".hos_warning",
            ".log_date", ".start_time", ".calculated_duration_minutes", ".is_certified",
            ".device_serial", ".vehicle_code", ".last_sync_at", ".action_label",
        })
            Assert.DoesNotContain(staleSnakeCase, page, StringComparison.Ordinal);
    }

    [Fact]
    public void DriverHosCertification_NormalizesApiTimestampsBeforeRenderingTheDailyDate()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "driver", "DriverHosPage.tsx"));

        Assert.Contains("function normalizedLogDate(value: unknown): string", page, StringComparison.Ordinal);
        Assert.Contains("parsed.toISOString().slice(0, 10)", page, StringComparison.Ordinal);
        Assert.Contains("const date = normalizedLogDate(row[\"logDate\"] ?? row[\"log_date\"]);", page, StringComparison.Ordinal);
        Assert.Contains("I certify that this daily HOS record is true and correct.", page, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticEldState_ExposesBothMalfunctionDeclarationAndRecoveryVerification()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "HosEldPage.tsx"));

        Assert.Contains("String(e.status) === \"Diagnostic\"", page, StringComparison.Ordinal);
        Assert.Contains("Record a confirmed malfunction from the diagnostic state", page, StringComparison.Ordinal);
        Assert.Contains("Verify recovery after a healthy provider sync", page, StringComparison.Ordinal);
        Assert.Contains("Mark Malfunction", page, StringComparison.Ordinal);
        Assert.Contains("Verify Recovery", page, StringComparison.Ordinal);
    }

    [Fact]
    public void CompositeHosEldPage_HonorsTheSeparateTelematicsEntitlement()
    {
        var page = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "pages", "HosEldPage.tsx"));
        var hooks = File.ReadAllText(Path.Combine(RepoRoot, "frontend", "src", "hooks", "useBatch6.ts"));

        Assert.Contains("useEldDevices(eldEntitled)", page, StringComparison.Ordinal);
        Assert.Contains("ELD device operations are not included", page, StringComparison.Ordinal);
        Assert.Contains("Hours-of-service records remain available", page, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"eld-entitlement-boundary\"", page, StringComparison.Ordinal);
        Assert.Contains("enabled });", hooks, StringComparison.Ordinal);
    }
}
