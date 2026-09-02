using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Opstrax.Api.Tests;

public sealed class AdminRolePermissionCatalogTests
{
    [Theory]
    [InlineData("telemetry.devices.manage")]
    [InlineData("telematics:devices:view")]
    [InlineData("telematics:devices:diagnostics")]
    [InlineData("telematics:devices:export")]
    [InlineData("telematics:gps:view")]
    [InlineData("telematics:gps:export")]
    [InlineData("telematics:diagnostics:export")]
    public void NarrowTelematicsPermissionsRemainAvailableToCustomRoles(string permission)
    {
        Assert.Contains(permission, EndpointMappings.CustomRolePermissionCatalog,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TenantAdminCanUseManageAndExportTheShippedGpsAndDiagnosticsSurfaces()
    {
        var permissions = EndpointMappings.RolePermissionDefaults["Tenant Admin"];
        var admin = Principal("Tenant Admin", permissions);

        Assert.Null(EndpointMappings.RequirePermission(admin, "telemetry.devices.read"));
        foreach (var permission in new[]
        {
            "telematics:gps:view",
            "telematics:gps:export",
            "telematics:diagnostics:view",
            "telematics:diagnostics:update",
            "telematics:diagnostics:export",
        })
        {
            Assert.Contains(permission, permissions, StringComparer.OrdinalIgnoreCase);
            Assert.Null(EndpointMappings.RequirePermission(admin, permission));
        }
    }

    [Fact]
    public void FleetManagerCanExportDevicesButDispatcherCannot()
    {
        Assert.Null(EndpointMappings.RequirePermission(
            Principal("Fleet Manager", EndpointMappings.RolePermissionDefaults["Fleet Manager"]),
            "telematics:devices:export"));

        var denied = EndpointMappings.RequirePermission(
            Principal("Dispatcher", EndpointMappings.RolePermissionDefaults["Dispatcher"]),
            "telematics:devices:export");
        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
    }

    [Fact]
    public void DispatcherCanReadDevicesButCannotManageOrExportThem()
    {
        var dispatcher = Principal("Dispatcher", EndpointMappings.RolePermissionDefaults["Dispatcher"]);

        Assert.Null(EndpointMappings.RequirePermission(dispatcher, "telemetry.devices.read"));
        foreach (var forbidden in new[] { "telemetry.devices.manage", "telematics:devices:export" })
        {
            var denied = EndpointMappings.RequirePermission(dispatcher, forbidden);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
    }

    [Fact]
    public void SafetyManagerCanReadTelematicsButCannotManageOrExportDeviceRegistry()
    {
        var safety = Principal("Safety Manager", EndpointMappings.RolePermissionDefaults["Safety Manager"]);
        foreach (var allowed in new[] { "telemetry.devices.read", "telematics:gps:view", "telematics:diagnostics:view", "telematics:sensors:view" })
            Assert.Null(EndpointMappings.RequirePermission(safety, allowed));
        foreach (var forbidden in new[] { "telemetry.devices.manage", "telematics:devices:export" })
        {
            var denied = EndpointMappings.RequirePermission(safety, forbidden);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
    }

    [Theory]
    [InlineData("Fleet Manager", "telematics:gps:view")]
    [InlineData("Fleet Manager", "telematics:diagnostics:view")]
    [InlineData("Fleet Manager", "telematics:sensors:view")]
    [InlineData("Dispatcher", "telematics:gps:view")]
    [InlineData("Maintenance Manager", "telematics:gps:view")]
    [InlineData("Maintenance Manager", "telematics:diagnostics:view")]
    [InlineData("Maintenance Manager", "telematics:sensors:view")]
    public void OperationalRolesCanOpenTheirShippedTelematicsReadRoutes(string role, string permission)
        => Assert.Null(EndpointMappings.RequirePermission(
            Principal(role, EndpointMappings.RolePermissionDefaults[role]), permission));

    [Fact]
    public void MaintenanceManagerCannotEnterTenantWideReportingSurfaces()
    {
        var permissions = EndpointMappings.RolePermissionDefaults["Maintenance Manager"];
        Assert.DoesNotContain("reports:view", permissions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("reports:export", permissions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("reports:manage", permissions, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("telemetry.recommendations.read", permissions, StringComparer.OrdinalIgnoreCase);

        var principal = Principal("Maintenance Manager", permissions);
        Assert.Null(EndpointMappings.RequirePermission(principal, "maintenance:view"));
        foreach (var forbidden in new[] { "reports:view", "reports:export", "reports:manage" })
        {
            var denied = EndpointMappings.RequirePermission(principal, forbidden);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
    }

    [Fact]
    public async Task MaintenanceManagerIsDeniedByRegisteredReportingRoutesBeforeDatabaseAccess()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var database = new Database(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Port=1;Database=authorization_guard;Username=none;Password=none;Timeout=1"
            }).Build());
        foreach (var serviceType in typeof(EndpointMappings).Assembly.GetTypes().Where(type =>
                     !type.IsGenericTypeDefinition &&
                     (type.Name.EndsWith("Service", StringComparison.Ordinal) ||
                      type.Name.EndsWith("Registry", StringComparison.Ordinal) ||
                      (type.IsInterface && type.Namespace?.StartsWith("Opstrax.Api", StringComparison.Ordinal) == true))))
        {
            builder.Services.AddSingleton(serviceType, _ =>
                throw new InvalidOperationException($"{serviceType.Name} must not be resolved by an authorization-denied route."));
        }
        builder.Services.AddSingleton<ICorrelationContext>(_ => null!);
        builder.Services.AddSingleton<IAuthorizationDecisionService>(_ => null!);
        builder.Services.AddSingleton<IAuditLogService>(_ => null!);
        builder.Services.AddSingleton(database);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[EndpointMappings.AuthCompanyIdItemKey] = 7001L;
            context.Items[EndpointMappings.AuthBranchIdItemKey] = 7011L;
            context.Items[EndpointMappings.AuthUserIdItemKey] = 7021L;
            context.Items[EndpointMappings.AuthRoleItemKey] = "Maintenance Manager";
            context.Items[EndpointMappings.AuthPermissionsItemKey] =
                EndpointMappings.RolePermissionDefaults["Maintenance Manager"];
            await next();
        });
        app.MapOpsTraxEndpoints();

        try
        {
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            foreach (var path in new[]
            {
                "/api/reports/scheduled",
                "/api/reports/ai/recommendations",
                "/api/predictions/driver-risk",
                "/api/carbon-emissions",
            })
            {
                using var response = await client.GetAsync(path);
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                    $"{path} returned {(int)response.StatusCode} {response.StatusCode}: {body}");
            }
        }
        finally
        {
            if (app.Lifetime.ApplicationStarted.IsCancellationRequested)
                await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("Driver")]
    [InlineData("Customer")]
    public void PortalAndPrivacyScopedRolesRemainClosedToBackOfficeTelematics(string role)
    {
        var principal = Principal(role, EndpointMappings.RolePermissionDefaults[role]);
        foreach (var permission in new[]
        {
            "telemetry.devices.read",
            "telematics:devices:export",
            "telematics:gps:view",
            "telematics:gps:export",
            "telematics:diagnostics:view",
            "telematics:diagnostics:update",
            "telematics:diagnostics:export",
            "telematics:sensors:view",
        })
        {
            var denied = EndpointMappings.RequirePermission(principal, permission);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
    }

    [Fact]
    public void ReadOnlyAuditorCanInspectButCannotChangeOrExportTelematics()
    {
        var auditor = Principal("Read-Only Auditor", EndpointMappings.RolePermissionDefaults["Read-Only Auditor"]);
        foreach (var allowed in new[] { "telemetry.devices.read", "telematics:gps:view", "telematics:diagnostics:view", "telematics:sensors:view" })
            Assert.Null(EndpointMappings.RequirePermission(auditor, allowed));
        foreach (var forbidden in new[] { "telemetry.devices.manage", "telematics:devices:export", "telematics:gps:export", "telematics:diagnostics:update", "telematics:sensors:update" })
        {
            var denied = EndpointMappings.RequirePermission(auditor, forbidden);
            Assert.Equal(StatusCodes.Status403Forbidden,
                Assert.IsAssignableFrom<IStatusCodeHttpResult>(denied).StatusCode);
        }
    }

    private static DefaultHttpContext Principal(string role, string[] permissions)
    {
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthUserIdItemKey] = 17L;
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = 23L;
        http.Items[EndpointMappings.AuthRoleItemKey] = role;
        http.Items[EndpointMappings.AuthPermissionsItemKey] = permissions;
        return http;
    }
}
