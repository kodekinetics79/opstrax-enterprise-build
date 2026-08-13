using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Foundation;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class Stage9DriverAuthorizationTests
{
    [Fact]
    public void GenericStage9MutationsDoNotTreatDriverSelfAsAnOperationsGrant()
    {
        var stage9 = ReadSource("backend-dotnet", "Controllers", "Stage9Endpoints.cs");

        Assert.DoesNotContain("\"driver:self\"", stage9, StringComparison.Ordinal);
        foreach (var permission in new[]
        {
            "operations.site_access.create", "operations.site_access.update",
            "operations.access_document.create", "operations.access_document.update",
            "operations.pickup_authorization.create", "operations.pickup_authorization.update",
            "operations.warehouse_handover.create", "operations.warehouse_handover.update",
            "operations.proof.create", "operations.proof.update", "operations.proof.submit",
            "operations.proof_artifact.create",
        })
        {
            Assert.Contains($"\"{permission}\"", stage9, StringComparison.Ordinal);
        }

        var driverEndpoints = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        Assert.Contains("RequirePermission(http, \"driver:self\")", driverEndpoints, StringComparison.Ordinal);
        Assert.Contains("/api/driver/assignments/{id:long}/proof", driverEndpoints, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DriverA_CannotMutateDriverBJobThroughAnyGenericStage9Route()
    {
        const long driverAUserId = 71_001;
        const long driverBJobId = 92_002;
        const long driverBEntityId = 93_002;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused",
            })
            .Build();
        var database = new Database(configuration);
        var correlation = new AmbientCorrelationContext();
        var stage9 = new Stage9OperationalFoundationService(
            database,
            new PostgresAiFoundationService(database, correlation),
            new PostgresApprovalWorkflowService(database, correlation),
            new PostgresDomainEventPublisher(database, correlation),
            new InMemoryIdempotencyService(),
            correlation);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(new AuditService(database));
        builder.Services.AddSingleton(stage9);
        await using var app = builder.Build();
        app.Use(async (http, next) =>
        {
            http.Items[EndpointMappings.AuthCompanyIdItemKey] = 70_001L;
            http.Items[EndpointMappings.AuthUserIdItemKey] = driverAUserId;
            http.Items[EndpointMappings.AuthRoleItemKey] = "Driver";
            http.Items[EndpointMappings.AuthPermissionsItemKey] = new[] { "driver:self" };
            await next();
        });
        app.MapStage9OperationsEndpoints();
        await app.StartAsync();

        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            var mutations = new (HttpMethod Method, string Path)[]
            {
                (HttpMethod.Post, $"/api/jobs/{driverBJobId}/site-access"),
                (HttpMethod.Patch, $"/api/site-access/{driverBEntityId}"),
                (HttpMethod.Post, $"/api/jobs/{driverBJobId}/access-documents"),
                (HttpMethod.Patch, $"/api/access-documents/{driverBEntityId}/status"),
                (HttpMethod.Post, $"/api/jobs/{driverBJobId}/pickup-authorizations"),
                (HttpMethod.Patch, $"/api/pickup-authorizations/{driverBEntityId}"),
                (HttpMethod.Post, $"/api/jobs/{driverBJobId}/warehouse-handovers"),
                (HttpMethod.Patch, $"/api/warehouse-handovers/{driverBEntityId}"),
                (HttpMethod.Post, $"/api/jobs/{driverBJobId}/proof-packages"),
                (HttpMethod.Patch, $"/api/proof-packages/{driverBEntityId}"),
                (HttpMethod.Post, $"/api/proof-packages/{driverBEntityId}/submit"),
                (HttpMethod.Post, $"/api/proof-packages/{driverBEntityId}/artifacts"),
            };

            foreach (var (method, path) in mutations)
            {
                using var request = new HttpRequestMessage(method, path)
                {
                    Content = JsonContent.Create(new { notes = "Driver A must not mutate Driver B work" }),
                };
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
