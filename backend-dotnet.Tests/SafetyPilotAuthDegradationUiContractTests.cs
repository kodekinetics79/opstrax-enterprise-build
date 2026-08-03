namespace Opstrax.Tests;

public sealed class SafetyPilotAuthDegradationUiContractTests
{
    [Fact]
    public void TenantAndPlatformAuthInvalidateCachesAndRevalidateRestoredDocuments()
    {
        var tenant = Read("frontend", "src", "hooks", "useAuth.tsx");
        Assert.Contains("clearAllSessionKeys()", tenant, StringComparison.Ordinal);
        Assert.Contains("queryClient.clear()", tenant, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"storage\"", tenant, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"pageshow\"", tenant, StringComparison.Ordinal);
        Assert.Contains("event.persisted", tenant, StringComparison.Ordinal);
        Assert.Contains("authApi.me()", tenant, StringComparison.Ordinal);

        var platform = Read("frontend", "src", "hooks", "usePlatformAuth.tsx");
        Assert.Contains("queryClient.clear()", platform, StringComparison.Ordinal);
        Assert.Contains("platformApi.me()", platform, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"storage\"", platform, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener(\"pageshow\"", platform, StringComparison.Ordinal);
        Assert.Contains("document.documentElement.style.visibility = \"hidden\"", platform, StringComparison.Ordinal);
        Assert.Contains("return { ...fresh, token: current.token }", platform, StringComparison.Ordinal);

        var platformEndpoints = Read("backend-dotnet", "Controllers", "PlatformEndpoints.cs");
        var platformMe = Block(platformEndpoints, "private static async Task<IResult> PlatformMe(", "private static async Task<IResult> PlatformLogout(");
        Assert.DoesNotContain("token = BearerToken(http)", platformMe, StringComparison.Ordinal);

        var program = Read("backend-dotnet", "Program.cs");
        Assert.Contains("context.Response.Headers.CacheControl = \"no-store, max-age=0\"", program, StringComparison.Ordinal);
        Assert.Contains("context.Response.Headers.Pragma = \"no-cache\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void SafetyLoadFailuresRemainDistinctAndOfferExplicitRetry()
    {
        var safety = Read("frontend", "src", "pages", "Batch4SafetyPage.tsx");
        Assert.Contains("rowsQuery.isError || summary.isError", safety, StringComparison.Ordinal);
        Assert.Contains("No empty or healthy state has been inferred", safety, StringComparison.Ordinal);
        Assert.Contains("void rowsQuery.refetch()", safety, StringComparison.Ordinal);
        Assert.Contains("void summary.refetch()", safety, StringComparison.Ordinal);

        var driverCoaching = Read("frontend", "src", "pages", "driver", "DriverCoachingPage.tsx");
        Assert.Contains("role=\"alert\"", driverCoaching, StringComparison.Ordinal);
        Assert.Contains("Retry coaching tasks", driverCoaching, StringComparison.Ordinal);
        Assert.Contains("onClick={() => void refetch()}", driverCoaching, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedDrawerFocusContractTrapsAndRestoresKeyboardFocus()
    {
        var hook = Read("frontend", "src", "hooks", "useDialogFocus.ts");
        Assert.Contains("event.key === \"Escape\"", hook, StringComparison.Ordinal);
        Assert.Contains("event.key !== \"Tab\"", hook, StringComparison.Ordinal);
        Assert.Contains("event.shiftKey", hook, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener(\"keydown\", onKeyDown, true)", hook, StringComparison.Ordinal);
        Assert.Contains("previous?.isConnected", hook, StringComparison.Ordinal);

        var ui = Read("frontend", "src", "components", "ui.tsx");
        var drawer = Block(ui, "export function DetailDrawer(", "export function LoadingState(");
        Assert.Contains("useDialogFocus<HTMLElement>(open, onClose)", drawer, StringComparison.Ordinal);
        Assert.Contains("ref={dialogRef}", drawer, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", drawer, StringComparison.Ordinal);
    }

    [Fact]
    public void CriticalSafetyAndDriverDialogsAdoptFocusNamingAndRecoveryContracts()
    {
        var safety = Read("frontend", "src", "pages", "Batch4SafetyPage.tsx");
        Assert.True(Count(safety, "useDialogFocus<") >= 5, "Every Safety custom dialog must adopt the shared focus contract");
        Assert.Contains("role=\"alert\"", safety, StringComparison.Ordinal);

        var dvir = Read("frontend", "src", "pages", "DvirInspectionsPage.tsx");
        Assert.True(Count(dvir, "useDialogFocus<") >= 2, "DVIR detail and create dialogs must adopt the shared focus contract");
        Assert.Contains("aria-label=\"DVIR report detail\"", dvir, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Create DVIR report\"", dvir, StringComparison.Ordinal);

        var hosEld = Read("frontend", "src", "pages", "HosEldPage.tsx");
        Assert.True(Count(hosEld, "useDialogFocus<") >= 3, "HOS/ELD custom dialogs must adopt the shared focus contract");
        Assert.Contains("aria-label=\"Driver HOS clock details\"", hosEld, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Mark ELD malfunction\"", hosEld, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Resolve ELD malfunction\"", hosEld, StringComparison.Ordinal);

        var driverDvir = Read("frontend", "src", "pages", "driver", "DriverDvirPage.tsx");
        Assert.True(Count(driverDvir, "useDialogFocus<") >= 2, "Driver DVIR dialogs must adopt the shared focus contract");
        Assert.Contains("role=\"alert\"", driverDvir, StringComparison.Ordinal);
        Assert.Contains("Retry certifications", driverDvir, StringComparison.Ordinal);
        Assert.Contains("disabled={reportsQ.isFetching}", driverDvir, StringComparison.Ordinal);

        var driverHos = Read("frontend", "src", "pages", "driver", "DriverHosPage.tsx");
        Assert.Contains("useDialogFocus<HTMLDivElement>(selected != null", driverHos, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Certify daily HOS record\"", driverHos, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", driverHos, StringComparison.Ordinal);
        Assert.Contains("onClick={() => void refetch()}", driverHos, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot(), .. parts]));

    private static int Count(string source, string value) => source.Split(value, StringSplitOptions.None).Length - 1;

    private static string Block(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"Missing source block {start}");
        return source[from..to];
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "frontend"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found");
    }
}
