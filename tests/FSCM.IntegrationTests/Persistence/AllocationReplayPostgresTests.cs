using System.Data.Common;
using FSCM.Application;
using FSCM.Application.Features.Orders;
using FSCM.Domain.Authorization;
using FSCM.Infrastructure;
using FSCM.Persistence;
using FSCM.Persistence.DbContexts;
using FSCM.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Persistence;

public sealed class AllocationReplayPostgresTests
{
    [PostgresWorkflowFact]
    public async Task A_serialization_retry_reloads_the_pool_and_commits_only_one_allocation_attempt()
    {
        var target=new Npgsql.NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("FSCM_TEST_POSTGRES"));
        Assert.True(target.Host is "localhost" or "127.0.0.1");Assert.StartsWith("fscm_e2e_",target.Database);
        target.Database+="_replay";
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["ConnectionStrings:DefaultConnection"]=target.ConnectionString,["SeedData:DefaultPassword"]="Local-E2e-Only!2049",
            ["Jwt:Secret"]=new string('r',64),["Jwt:Issuer"]="FSCM.API",["Jwt:Audience"]="FSCM.Clients" }).Build();
        var fault=new SerializationFault();
        var services=new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddApplicationServices().AddPersistenceServices(config).AddInfrastructureServices(config);
        services.AddDbContext<AppDbContext>(o=>o.AddInterceptors(fault));
        await using var provider=services.BuildServiceProvider();
        await using(var setup=provider.CreateAsyncScope()) {
            var db=setup.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Database.CanConnectAsync(),"An existing database must not be modified.");
            await setup.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
        }
        Guid orderId;
        await using(var request=provider.CreateAsyncScope()) {
            var db=request.ServiceProvider.GetRequiredService<AppDbContext>();
            var retailer=await db.RetailerRecords.SingleAsync(r=>r.Code=="10004127");
            var sales=await db.UserAccountRecords.SingleAsync(u=>u.Email=="sales@fscm.local");
            var product=await db.ProductRecords.SingleAsync(p=>p.Sku=="SKU001");
            fault.Armed=true;
            var order=await request.ServiceProvider.GetRequiredService<OrderService>().CreateAsync(
                new(Guid.NewGuid(),retailer.Id,"Credit",null,null,"Serialization replay",[new(product.Id,1)]),sales.Id,default);
            orderId=order.Id;
            Assert.Equal(OrderStatuses.Quoted,order.Status);
        }
        Assert.Equal(1,fault.Failures);
        await using var verify=provider.CreateAsyncScope();
        var read=verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await read.AllocationRunRecords.Where(r=>r.SalesOrderId==orderId).ToArrayAsync());
        Assert.Single(await read.SalesOrderQuoteRecords.Where(q=>q.SalesOrderId==orderId).ToArrayAsync());
        var lines=read.SalesOrderLineRecords.Where(l=>l.SalesOrderId==orderId).Select(l=>l.Id);
        var allocations=await read.AllocationRecords.Where(a=>lines.Contains(a.SalesOrderLineId)).ToArrayAsync();
        Assert.Equal(1,allocations.Sum(a=>a.AllocatedQuantity));
        Assert.Equal(1,await read.ReservationRecords.Where(r=>allocations.Select(a=>(Guid?)a.Id).Contains(r.AllocationId)).SumAsync(r=>r.Quantity));
    }

    private sealed class SerializationFault : DbCommandInterceptor
    {
        public bool Armed; public int Failures;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
        {
            if(Armed && command.CommandText.Contains("INSERT INTO \"ALLOCATION_RUN\"",StringComparison.Ordinal)) {
                Armed=false;Failures++;
                throw new Npgsql.PostgresException("Simulated serialization conflict","ERROR","ERROR","40001");
            }
            return base.ReaderExecutingAsync(command,eventData,result,ct);
        }
    }
}
