using System.IO;
using Opstrax.Api.Controllers;
using Xunit;

namespace Opstrax.Tests;

// Jobs module contract checks.
// These tests document the live seed and RBAC baseline that the frontend now
// depends on after removing the demo fallback path.

public class JobsSeedPermissionTests
{
    [Fact]
    public void DispatcherSeed_Includes_JobsView_And_JobsManage()
    {
        var seed = ReadSeedSql();
        Assert.Contains("dispatch:view','dispatch:manage'", seed);
        Assert.Contains("jobs:view','jobs:manage'", seed);
    }

    [Fact]
    public void DriverSeed_Includes_JobsView_But_Not_JobsManage()
    {
        var seed = ReadSeedSql();
        Assert.Contains("driver:portal','jobs:view','dvir:manage'", seed);
        Assert.DoesNotContain("driver:portal','jobs:view','jobs:manage'", seed);
    }

    [Fact]
    public void JobsDeleteRoute_Requires_DispatchManage()
    {
        Assert.Contains("*", EndpointMappings.RolePermissionDefaults["Super Admin"]);
        Assert.Contains("*", EndpointMappings.RolePermissionDefaults["Company Admin"]);
        Assert.DoesNotContain("dispatch:manage", EndpointMappings.RolePermissionDefaults["Dispatcher"]);
    }

    private static string ReadSeedSql()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "database", "init", "002_seed.sql")))
        {
            root = root.Parent;
        }

        if (root is null)
            throw new FileNotFoundException("Could not locate database/init/002_seed.sql from test output directory");

        var path = Path.Combine(root.FullName, "database", "init", "002_seed.sql");
        return File.ReadAllText(path);
    }
}
