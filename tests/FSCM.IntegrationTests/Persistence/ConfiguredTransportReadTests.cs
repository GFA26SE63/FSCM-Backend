using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Shipments;
using FSCM.Domain.Authorization;
using FSCM.IntegrationTests.Api;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Shipments;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FSCM.IntegrationTests.Persistence;

public sealed class ConfiguredTransportReadTests
{
    [ConfiguredDatabaseFact]
    public async Task Keeper_delivery_screen_grants_departure_but_enforces_live_warehouse_scope()
    {
        await using var db = new AppDbContextFactory().CreateDbContext([]);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True(await db.ScreenApiMappings.AnyAsync(m => m.BusinessRole == SystemRoles.WarehouseKeeper &&
            m.ScreenCode == "readiness" && m.ApiCode == "api:Shipments.Dispatch"));
        Assert.True(await db.ScreenApiMappings.AnyAsync(m => m.BusinessRole == SystemRoles.WarehouseKeeper &&
            m.ScreenCode == "readiness" && (m.ApiCode == "api:Shipments.Assign" || m.ApiCode == "api:Shipments.ResolveException")));
        Assert.False(await db.RoleDefinitionRecords.AnyAsync(r => r.BaseRole == SystemRoles.WarehouseKeeper &&
            db.RolePermissionRecords.Any(p => p.RoleId == r.Id && p.PermissionKey == "warehouse-outbound") &&
            !db.RolePermissionRecords.Any(p => p.RoleId == r.Id && p.PermissionKey == "readiness")));
        using var identity = new IdentityAuthorizationTests.IdentityApiFactory();
        identity.SetPermissions(SystemRoles.WarehouseKeeper, "readiness", "api:Shipments.Dispatch", "api:Shipments.Assign", "api:Shipments.OrderReadiness", "api:PickingLists.List", "api:Shipments.List", "api:Orders.List", "api:Warehouses.List", "api:TransportDispatches.Read");
        using var factory = identity.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.AddSingleton(db);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(identity.WarehouseKeeper));
        var read = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query { entities { deliveryPickingReads { id orderId } shipments(first: 1) { nodes { id warehouseId status } } salesOrders(first: 1) { nodes { id orderCode } } warehouses(first: 1) { nodes { id code } } drivers(first: 1) { nodes { id warehouseId code fullName isActive } } vehicles(first: 1) { nodes { id warehouseId plateNumber vehicleType capacity isActive } } } }"
        });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        var missing = await client.PostAsJsonAsync($"/api/v1/shipments/{Guid.NewGuid()}/dispatch", new { expectedVersion = 0 });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        // This test account has no warehouse assignments. Even a known trip must be rejected
        // before checking its status or returning an idempotent successful departure.
        var trip = await db.ShipmentRecords.AsNoTracking().FirstOrDefaultAsync();
        if (trip is not null)
        {
            var forbidden = await client.PostAsJsonAsync($"/api/v1/shipments/{trip.Id}/dispatch", new { expectedVersion = trip.Version });
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            var assignment = await client.PatchAsJsonAsync($"/api/v1/shipments/{trip.Id}/assignment",
                new { driverId = Guid.Empty, vehicleId = Guid.Empty, expectedVersion = trip.Version });
            Assert.Equal(HttpStatusCode.Forbidden, assignment.StatusCode);
        }
    }

    [ConfiguredDatabaseFact]
    public async Task Dispatch_screen_reads_resource_availability_and_lifecycle_without_transfer_screen_grants()
    {
        await using var db = new AppDbContextFactory().CreateDbContext([]);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, await db.ScreenApiMappings.CountAsync(m =>
            (m.BusinessRole == SystemRoles.Admin || m.BusinessRole == SystemRoles.Operator) &&
            m.ScreenCode == "readiness" && m.ApiCode == "api:Shipments.Dispatch"));
        Assert.False(await db.RoleDefinitionRecords.AnyAsync(role =>
            (role.BaseRole == SystemRoles.Admin || role.BaseRole == SystemRoles.Operator) &&
            db.RolePermissionRecords.Any(p => p.RoleId == role.Id && p.PermissionKey == "readiness") &&
            !db.RolePermissionRecords.Any(p => p.RoleId == role.Id && p.PermissionKey == "api:Shipments.Dispatch")));
        using var identity = new IdentityAuthorizationTests.IdentityApiFactory();
        var actor = identity.AddTestUser("dispatch-read@test.invalid", SystemRoles.Operator);
        identity.SetPermissions(SystemRoles.Operator, "readiness", "api:Shipments.Dispatch", "api:Shipments.Assign", "api:Shipments.OrderReadiness", "api:PickingLists.List", "api:Shipments.List", "api:Orders.List", "api:Warehouses.List", "api:TransportDispatches.Read");
        using var factory = identity.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.AddSingleton(db);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identity.CreateToken(actor));

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "query { entities { busyTransportRead { driverIds vehicleIds } } }"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("errors", out _), json.RootElement.ToString());
        var busy = json.RootElement.GetProperty("data").GetProperty("entities").GetProperty("busyTransportRead");
        var expected = await new ShipmentStore(db).ListBusyTransportAsync(default, actor.Id, SystemRoles.Operator);
        Assert.Equal(expected.DriverIds.Order(), busy.GetProperty("driverIds").EnumerateArray().Select(x => x.GetGuid()).Order());
        Assert.Equal(expected.VehicleIds.Order(), busy.GetProperty("vehicleIds").EnumerateArray().Select(x => x.GetGuid()).Order());

        // Reach domain validation using the dispatch screen alone, without touching real trips.
        var departure = await client.PostAsJsonAsync($"/api/v1/shipments/{Guid.NewGuid()}/dispatch",
            new { expectedVersion = 0 });
        Assert.Equal(HttpStatusCode.NotFound, departure.StatusCode);

        var orderId = await db.ShipmentRecords.AsNoTracking().Select(x => (Guid?)x.SalesOrderId).FirstOrDefaultAsync();
        if (orderId.HasValue)
        {
            var readinessResponse = await client.PostAsJsonAsync("/graphql", new
            {
                query = "query($orderId: UUID!) { entities { shipmentReadinessRead(orderId: $orderId) { salesOrderId dispatchStatus isHistory parts { warehouseId status } } } }",
                variables = new { orderId }
            });
            Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);
            using var readiness = JsonDocument.Parse(await readinessResponse.Content.ReadAsStringAsync());
            Assert.False(readiness.RootElement.TryGetProperty("errors", out _), readiness.RootElement.ToString());
            var result = readiness.RootElement.GetProperty("data").GetProperty("entities").GetProperty("shipmentReadinessRead");
            Assert.Equal(orderId.Value, result.GetProperty("salesOrderId").GetGuid());
            Assert.Equal(result.GetProperty("dispatchStatus").GetString() == "Delivered", result.GetProperty("isHistory").GetBoolean());
        }
    }
}
