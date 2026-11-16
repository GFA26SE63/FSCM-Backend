using FSCM.Application;
using FSCM.Application.Features.Dashboard;
using FSCM.Application.Features.Orders;
using FSCM.Application.Features.Reports;
using FSCM.Domain.Authorization;
using FSCM.Domain.Entities;
using FSCM.Infrastructure;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Inventory;
using FSCM.Persistence.Reports;
using FSCM.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Persistence;

public sealed class DashboardPostgresTests
{
    [PostgresWorkflowFact]
    public async Task Dashboard_enforces_live_region_grants_and_uses_real_stock_and_payment_ledgers()
    {
        var target = new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES"));
        Assert.True(target.Host is "127.0.0.1" or "localhost");
        Assert.StartsWith("fscm_e2e_", target.Database);
        target.Database += "_dash";
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = target.ConnectionString,
            ["SeedData:DefaultPassword"] = "Local-E2e-Only!2049",
            ["Jwt:Secret"] = new string('d', 64), ["Jwt:Issuer"] = "FSCM.API", ["Jwt:Audience"] = "FSCM.Clients"
        }).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Database.CanConnectAsync(), "Use a new disposable database name. Existing databases must not be modified.");
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.AddHours(7).DateTime);
        var users = await db.UserAccountRecords.ToArrayAsync();
        var admin = users.Single(u => u.Email == "admin@fscm.local");
        var op = users.Single(u => u.Email == "operator@fscm.local");
        var keeper = users.Single(u => u.Email == "warehouse.keeper@fscm.local");
        var manager = users.Single(u => u.Email == "sales.manager@fscm.local");
        var sales = users.Single(u => u.Email == "sales@fscm.local");
        var retailerUser = users.Single(u => u.Email == "retailer@fscm.local");
        foreach (var grant in await db.UserWarehouseAssignmentRecords.Where(a => a.UserAccountId == keeper.Id).ToArrayAsync()) grant.IsActive = false;
        foreach (var grant in await db.UserRegionAssignmentRecords.Where(a => a.UserAccountId == keeper.Id || a.UserAccountId == manager.Id).ToArrayAsync()) grant.IsActive = false;
        foreach (var member in await db.SalesGroupMemberRecords.Where(a => a.UserAccountId == manager.Id || a.UserAccountId == sales.Id).ToArrayAsync()) member.IsActive = false;
        await db.SaveChangesAsync();
        UserAccount Person(string code, string role) => new() { Id=Guid.NewGuid(),Email=$"{code}@dashboard.test",FullName=code,Role=role,IdentityProvider="Local",IsActive=true,CreatedAt=now,UpdatedAt=now };
        var idle = Person("idle", SystemRoles.Sales); var foreignSales = Person("foreign", SystemRoles.Sales);
        var empty = Person("empty", SystemRoles.WarehouseKeeper);
        db.AddRange(idle, foreignSales, empty);
        Region Region(string code) => new() { Id=Guid.NewGuid(),Code=code,Name=code,IsActive=true,CreatedAt=now,UpdatedAt=now };
        var a = Region("DASH-A"); var b = Region("DASH-B"); db.AddRange(a,b);
        Warehouse Warehouse(string code, Guid region) => new() { Id=Guid.NewGuid(),Code=code,Name=code,RegionId=region,IsActive=true,CreatedAt=now,UpdatedAt=now };
        var wa = Warehouse("DASH-WA",a.Id); var sibling = Warehouse("DASH-WA2",a.Id); var wb=Warehouse("DASH-WB",b.Id); db.AddRange(wa,sibling,wb);
        SalesGroup Group(string code, Guid region) => new() { Id=Guid.NewGuid(),Code=code,Name=code,RegionId=region,IsActive=true,CreatedAt=now,UpdatedAt=now };
        var ga=Group("DASH-GA",a.Id); var gb=Group("DASH-GB",b.Id); db.AddRange(ga,gb);
        db.AddRange(new UserRegionAssignment { Id=Guid.NewGuid(),UserAccountId=keeper.Id,RegionId=a.Id,IsActive=true,AssignedAt=now },
            new UserRegionAssignment { Id=Guid.NewGuid(),UserAccountId=manager.Id,RegionId=a.Id,IsActive=true,AssignedAt=now });
        foreach(var (person,group) in new[]{(sales,ga),(idle,ga),(foreignSales,gb)})
            db.Add(new SalesGroupMember { Id=Guid.NewGuid(),UserAccountId=person.Id,SalesGroupId=group.Id,IsActive=true,AssignedAt=now });
        var retailer=await db.RetailerRecords.FirstAsync();
        var retailerAssignment=await db.RetailerSalesAssignmentRecords.SingleAsync(r=>r.RetailerId==retailer.Id && r.IsActive);
        retailerAssignment.SalesGroupId=ga.Id;
        SalesOrder Order(string code,Guid group,Guid creator,string status,decimal amount) => new() { Id=Guid.NewGuid(),OrderCode=code,RetailerId=retailer.Id,RetailerCodeSnapshot=retailer.Code,RetailerNameSnapshot=retailer.Name,SalesGroupId=group,CreatedByUserId=creator,ClientOrderKey=Guid.NewGuid(),RequestHash=code,Source="Online",Status=status,PaymentMethod="Credit",DeliveryAddress="Dashboard test",FinalAmount=amount,SubtotalAmount=amount,ServerReceivedAt=now.AddDays(-40),AcceptedAt=now.AddDays(-40),CreatedAt=now.AddDays(-40),UpdatedAt=now,Version=1 };
        var oa=Order("DASH-OA",ga.Id,sales.Id,OrderStatuses.Completed,1000);
        var ob=Order("DASH-OB",gb.Id,foreignSales.Id,OrderStatuses.Completed,9000);
        var self=Order("DASH-SELF",ga.Id,retailerUser.Id,OrderStatuses.Completed,200);
        var delivered=Order("DASH-DEL",ga.Id,sales.Id,OrderStatuses.Delivered,300);
        var old=Order("DASH-OLD",ga.Id,sales.Id,OrderStatuses.Completed,8000);
        db.AddRange(oa,ob,self,delivered,old);
        var template=await db.SalesOrderLineRecords.FirstAsync();
        foreach(var order in new[]{oa,self})
        {
            var line=System.Text.Json.JsonSerializer.Deserialize<SalesOrderLine>(System.Text.Json.JsonSerializer.Serialize(template))!;
            line.Id=Guid.NewGuid();line.SalesOrderId=order.Id;line.LineNumber=1;line.OrderedQuantity=2;
            // Canonical line total intentionally differs from the historical header total.
            line.NetAmount=order.FinalAmount+50;line.GrossAmount=line.NetAmount;line.UnitPrice=line.NetAmount/2;line.DiscountAmount=0;
            db.Add(line);
        }
        foreach(var order in new[]{oa,ob,self,old}) db.Add(new OrderStatusHistory { Id=Guid.NewGuid(),SalesOrderId=order.Id,ChangedByUserId=op.Id,ToStatus=OrderStatuses.Completed,ChangedAt=order==old?now.AddDays(-40):now });
        // A previous completion must not override the latest close date.
        db.Add(new OrderStatusHistory { Id=Guid.NewGuid(),SalesOrderId=oa.Id,ChangedByUserId=op.Id,ToStatus=OrderStatuses.Completed,ChangedAt=now.AddDays(-40) });
        Payment Payment(SalesOrder order,decimal adjusted,decimal paid) => new() { Id=Guid.NewGuid(),SalesOrderId=order.Id,Method="Credit",Status=paid>0?"PartiallyPaid":"Unpaid",Amount=order.FinalAmount,AdjustedAmount=adjusted,ConfirmedAmount=paid,DeliveredDate=today.AddDays(-2),DueDate=today.AddDays(-1),ConfirmedAt=paid>0?now:null,FinalApprovedAt=now,FinalApprovalReference=order.OrderCode,CreatedAt=now,UpdatedAt=now,Version=1 };
        var pa=Payment(oa,900,40);var pd=Payment(delivered,300,30); db.AddRange(pa,pd);
        var receipt=new PaymentReceipt { Id=Guid.NewGuid(),ReceiptCode="DASH-R",RetailerId=retailer.Id,ReceivedAmount=50,UnappliedCreditAmount=10,Status=PaymentReceiptStatus.Confirmed,ConfirmedAt=now,ConfirmedByUserId=op.Id,OperationKey="DASH-R",CreatedAt=now,UpdatedAt=now,Version=1 };
        db.Add(receipt); db.Add(new PaymentReceiptAllocation { Id=Guid.NewGuid(),PaymentId=pa.Id,PaymentReceiptId=receipt.Id,Amount=40,CreatedAt=now });
        var product=await db.ProductRecords.FirstAsync();product.NearDateThresholdHours=48;
        Batch Batch(string code,Guid warehouse,DateOnly expiry) => new() { Id=Guid.NewGuid(),BatchCode=code,WarehouseId=warehouse,ProductId=product.Id,ProductionDate=today.AddDays(-5),ExpirationDate=expiry,ExpiryStatus="Valid",StockStatus="InStock",Quantity=100,Version=1,CreatedAt=now,UpdatedAt=now };
        var near=Batch("DASH-NEAR",wa.Id,today.AddDays(1));var expired=Batch("DASH-EXP",wa.Id,today.AddDays(-1));var foreign=Batch("DASH-FOREIGN",wb.Id,today.AddDays(1));
        db.AddRange(near,expired,foreign);
        foreach(var (batch,qty,reserved) in new[]{(near,100m,20m),(expired,7m,0m),(foreign,999m,0m)}) db.Add(new InventoryBalance { Id=Guid.NewGuid(),BatchId=batch.Id,PhysicalQuantity=qty,ReservedQuantity=reserved,AvailableQuantity=qty-reserved,Version=1,UpdatedAt=now });
        db.Add(new InventoryLock { Id=Guid.NewGuid(),BatchId=near.Id,WarehouseId=wa.Id,ProductId=product.Id,LockedQuantity=15,Status="Active",Reason="Damaged",Version=1,CreatedAt=now,UpdatedAt=now });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        // Retailer self-orders remain business revenue/product sales, but are not Sales KPI rows.
        var reports = new ReportStore(db);
        var kpiOrders = await reports.ListRecognizedOrdersAsync(today, today, [ga.Id], null, default);
        var salesOrder = Assert.Single(kpiOrders);
        Assert.Equal(oa.Id, salesOrder.OrderId);
        Assert.Equal(sales.Id, salesOrder.SalesUserId);
        Assert.Equal(900m, salesOrder.RecognizedRevenue);
        Assert.Empty(await reports.ListRecognizedOrdersAsync(today, today, [ga.Id], retailerUser.Id, default));
        var ranking = Assert.Single(await reports.AggregateSalesRevenueAsync(today, today, [ga.Id], default));
        Assert.Equal(sales.Id, ranking.SalesUserId);
        Assert.Equal(900m, ranking.RecognizedRevenue);
        var dashboard=scope.ServiceProvider.GetRequiredService<DashboardService>();
        var query=new DashboardQuery(today,today);
        async Task<DashboardResult> Read(UserAccount person,DashboardQuery? filter=null) => await dashboard.GetAsync(person.Id,person.Role,filter??query,default);
        decimal Card(DashboardResult result,string key)=>result.Cards.Single(c=>c.Key==key).Value;
        var keeperData=await Read(keeper);
        Assert.False(keeperData.Scope.IsGlobal);Assert.Equal(2,keeperData.Scope.Warehouses.Count);
        var stock=Assert.Single(keeperData.Inventory,w=>w.Id==wa.Id);Assert.Equal(wa.Id,stock.Id);
        Assert.Equal(107,stock.OnHand);Assert.Equal(65,stock.Available);Assert.Equal(100,stock.NearExpiry);Assert.Equal(7,stock.Expired);
        Assert.Empty(keeperData.SalesTeam);Assert.Empty(keeperData.RevenueByDay);Assert.Null(keeperData.Health);
        Assert.DoesNotContain(keeperData.Cards,c=>c.Key=="revenue");
        Assert.All(keeperData.ExpiryRisks,r=>Assert.NotEqual("DASH-FOREIGN",r.BatchCode));
        Assert.True(await new InventoryStore(db).CanAccessWarehouseAsync(keeper.Id,keeper.Role,sibling.Id,default));
        Assert.False(await new InventoryStore(db).CanAccessWarehouseAsync(keeper.Id,keeper.Role,wb.Id,default));
        Assert.DoesNotContain(await new InventoryStore(db).ListInventoryAsync(keeper.Id,keeper.Role,null,null,default),r=>r.Batch.WarehouseId==wb.Id);
        var managerData=await Read(manager);
        Assert.Single(managerData.Scope.SalesGroups);Assert.Equal(ga.Id,managerData.Scope.SalesGroups.Single().Id);
        Assert.Equal(1100,Card(managerData,"revenue"));Assert.Equal(2,Card(managerData,"completed"));
        Assert.Equal(70,Card(managerData,"collected")); // Receipt allocation 40 + legacy 30, never count 40 twice.
        Assert.Equal(1130,Card(managerData,"outstanding"));Assert.Equal(1130,Card(managerData,"overdue"));
        Assert.Equal(200,managerData.UnattributedRevenue);
        Assert.Equal(1100,Assert.Single(managerData.TopProducts).Revenue);
        Assert.Contains(managerData.SalesTeam,p=>p.Id==idle.Id && p.Revenue==0 && p.CompletedOrders==0);
        Assert.DoesNotContain(managerData.SalesTeam,p=>p.Id==foreignSales.Id || p.Id==retailerUser.Id);
        Assert.Empty(managerData.Inventory);Assert.Null(managerData.Health);
        Assert.True(await new ReportStore(db).IsActiveGroupMemberAsync(manager.Id,ga.Id,default));
        var adminData=await Read(admin);var operatorData=await Read(op);
        Assert.True(adminData.Scope.IsGlobal);Assert.True(operatorData.Scope.IsGlobal);
        Assert.Equal(Card(adminData,"revenue"),Card(operatorData,"revenue"));
        Assert.Contains(operatorData.Inventory,w=>w.Id==wb.Id);Assert.Contains(operatorData.SalesTeam,p=>p.Id==foreignSales.Id);
        Assert.Equal(70,Card(await Read(op,query with { RegionId=a.Id }),"collected"));
        Assert.True(Card(operatorData,"collected") >= 80); // Global includes the unapplied 10.
        var noScope=await Read(empty);Assert.False(noScope.Scope.HasAssignments);Assert.Empty(noScope.Inventory);Assert.Empty(noScope.WorkItems);Assert.All(noScope.Cards,c=>Assert.Equal(0,c.Value));
        foreach(var (person,filter) in new[]{(keeper,query with { WarehouseId=wb.Id }),(keeper,query with { RegionId=b.Id }),(manager,query with { SalesGroupId=gb.Id }),(manager,query with { RegionId=b.Id })})
            Assert.Equal("forbidden",(await Assert.ThrowsAsync<ReportOperationException>(()=>Read(person,filter))).Code);
        Assert.Equal("forbidden",(await Assert.ThrowsAsync<ReportOperationException>(()=>dashboard.GetAsync(keeper.Id,SystemRoles.Admin,query,default))).Code);
        // Revocation takes effect with the same actor/session; a warehouse's region label never grants sibling warehouses.
        var regionGrant=await db.UserRegionAssignmentRecords.SingleAsync(a=>a.UserAccountId==keeper.Id && a.IsActive);regionGrant.IsActive=false;
        db.Add(new UserWarehouseAssignment { Id=Guid.NewGuid(),UserAccountId=keeper.Id,WarehouseId=wa.Id,IsActive=true,AssignedAt=now });
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        Assert.Single((await Read(keeper)).Scope.Warehouses);
        Assert.Equal("forbidden",(await Assert.ThrowsAsync<ReportOperationException>(()=>Read(keeper,query with { WarehouseId=sibling.Id }))).Code);
        var account=await db.UserAccountRecords.SingleAsync(u=>u.Id==keeper.Id);account.IsActive=false;await db.SaveChangesAsync();db.ChangeTracker.Clear();
        Assert.Equal("forbidden",(await Assert.ThrowsAsync<ReportOperationException>(()=>Read(keeper))).Code);
    }
}
