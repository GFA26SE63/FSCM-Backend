using System.Collections;
using System.Security.Claims;
using FSCM.Application.Features.Deliveries;
using FSCM.Application.Features.EntityQueries;
using FSCM.Application.Features.Identity;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Persistence.DbContexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace FSCM.API.Security;

/// <summary>
/// Permission delegation never expands a business data scope. Reuse the same scoped
/// queries as GraphQL before services run, including nested financial allocations.
/// Services retain their own ownership, warehouse and state/concurrency checks.
/// </summary>
public sealed class ResourceScopeFilter(IIdentityStore identities, IEntityQueryService queries,
    AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action ||
            context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null)
        { await next(); return; }
        if (action.ControllerName is not ("Orders" or "Shipments" or "Deliveries" or "Payments" or "PaymentReceipts" or
            "Refunds" or "Complaints" or "InventoryLocks" or "GoodsReceipts" or "InternalTransfers" or "WarehouseDocuments" or "Retailers"))
        { await next(); return; }
        var user = context.HttpContext.User;
        var role = user.FindFirstValue(ClaimTypes.Role) ?? "";
        // These are global data-scope profiles, not permission to execute an operation.
        if (role is SystemRoles.Admin or SystemRoles.Operator ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
        { await next(); return; }
        var ct = context.HttpContext.RequestAborted;
        var access = new EntityQueryAccess(actor, role, await identities.GetScopeAsync(actor, ct),
            user.FindAll(PermissionAuthorizationHandler.GrantClaim).Select(c => c.Value).ToArray());

        async Task<bool> Check<T>(Guid id) where T : class
        {
            if (id == Guid.Empty) return true; // leave malformed requests to domain validation
            var scoped = queries.Query<T>(access);
            var allowed = scoped.Provider is IAsyncQueryProvider
                ? await scoped.AnyAsync(x => EF.Property<Guid>(x, "Id") == id, ct)
                : scoped.AsEnumerable().Any(x => (Guid?)x.GetType().GetProperty("Id")?.GetValue(x) == id);
            if (allowed) return true;
            // A missing resource must still reach its normal 404/validation contract.
            return !await db.Set<T>().AsNoTracking().AnyAsync(x => EF.Property<Guid>(x, "Id") == id, ct);
        }
        async Task<bool> ShipmentCommand(Guid id)
        {
            if (action.ActionName is not ("Assign" or "FlagException" or "ResolveException")) return await Check<Shipment>(id);
            var warehouse = await db.ShipmentRecords.AsNoTracking().Where(x => x.Id == id)
                .Select(x => (Guid?)x.WarehouseId).SingleOrDefaultAsync(ct);
            return !warehouse.HasValue || access.Scope.WarehouseIds.Contains(warehouse.Value);
        }
        async Task<bool> Named(string name, Guid id) => name switch
        {
            "OrderId" or "OrderIds" or "SalesOrderId" or "orderId" => await Check<SalesOrder>(id),
            "PaymentId" or "paymentId" => await Check<Payment>(id),
            "receiptId" => await Check<PaymentReceipt>(id),
            "RetailerId" or "retailerId" => await Check<Retailer>(id),
            "WarehouseId" or "SourceWarehouseId" or "warehouseId" => id == Guid.Empty ||
                access.Scope.WarehouseIds.Contains(id) || !await db.WarehouseRecords.AnyAsync(x => x.Id == id, ct),
            "BatchId" => await Check<FSCM.Domain.Entities.Batch>(id),
            "ShipmentId" or "shipmentId" => await Check<Shipment>(id),
            "DriverId" => await Check<Driver>(id),
            "VehicleId" => await Check<Vehicle>(id),
            "ReceiptDiscrepancyId" => await Check<ReceiptDiscrepancy>(id),
            "id" => action.ControllerName switch
            {
                "Orders" => await Check<SalesOrder>(id),
                "Retailers" => await Check<Retailer>(id),
                "Shipments" => await ShipmentCommand(id),
                "Payments" => await Check<Payment>(id),
                "Refunds" => await Check<ManualRefund>(id),
                "Complaints" => await Check<ReceiptDiscrepancy>(id),
                "InventoryLocks" => await Check<InventoryLock>(id),
                "GoodsReceipts" => await Check<GoodsReceipt>(id),
                "WarehouseDocuments" => await Check<WarehouseDocument>(id),
                "Deliveries" when action.ActionName.Contains("Driver", StringComparison.Ordinal) => await Check<Driver>(id),
                "Deliveries" when action.ActionName.Contains("Vehicle", StringComparison.Ordinal) => await Check<Vehicle>(id),
                "InternalTransfers" => await Check<WarehouseTransfer>(id),
                _ => true
            },
            _ => true
        };
        async Task<bool> References(object? value, string name, int depth = 0)
        {
            if (value is Guid id) return await Named(name, id);
            if (value is null || value is string || depth > 4 || value.GetType().IsPrimitive || value.GetType().IsEnum) return true;
            if (value is IEnumerable list)
            {
                foreach (var item in list) if (!await References(item, name, depth + 1)) return false;
                return true;
            }
            if (value.GetType().Namespace?.StartsWith("FSCM.API.Contracts", StringComparison.Ordinal) != true) return true;
            foreach (var property in value.GetType().GetProperties())
                if (!await References(property.GetValue(value), property.Name, depth + 1)) return false;
            return true;
        }
        // Master-data and account management are governed by their operation grants and
        // target-account invariants, rather than transaction ownership.
        if (action.ControllerName is "Orders" or "Shipments" or "Deliveries" or "Payments" or "PaymentReceipts" or
            "Refunds" or "Complaints" or "InventoryLocks" or "GoodsReceipts" or "InternalTransfers" or "WarehouseDocuments" or "Retailers")
            foreach (var argument in context.ActionArguments)
                if (!await References(argument.Value, argument.Key))
                { context.Result = new ForbidResult(); return; }

        var executed = await next();
        if (executed.Result is ObjectResult result && action.ControllerName == "Deliveries")
        {
            if (result.Value is IReadOnlyCollection<DriverResult> drivers)
                result.Value = drivers.Where(x => access.Scope.WarehouseIds.Contains(x.WarehouseId)).ToArray();
            if (result.Value is IReadOnlyCollection<VehicleResult> vehicles)
                result.Value = vehicles.Where(x => access.Scope.WarehouseIds.Contains(x.WarehouseId)).ToArray();
        }
    }
}
