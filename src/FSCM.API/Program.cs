using FSCM.API.Extensions;
using FSCM.Application;
using FSCM.Infrastructure;
using FSCM.Persistence;
using FSCM.Persistence.Seeding;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddFmcgConfiguration(builder.Environment);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddApiServices(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.AddInfrastructureServices(builder.Configuration);

var app = builder.Build();

if (args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Development data can only be seeded in the Development environment.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
    await seeder.SeedAsync();
    return;
}

app.UseApiPipeline();

app.MapApiEndpoints();

app.Run();

public partial class Program;
