using FSCM.API.Extensions;
using FSCM.Application;
using FSCM.Infrastructure;
using FSCM.Persistence;
using Serilog;

// Reject retired provisioning commands before building the host or opening a database connection.
if (args.Any(argument => argument.Equals("--seed", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--seed=", StringComparison.OrdinalIgnoreCase) ||
        argument.Equals("--bootstrap-admin", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("--bootstrap-admin=", StringComparison.OrdinalIgnoreCase)))
    throw new NotSupportedException("Backend data seeding is disabled. Prepare data using reviewed SQL scripts in DBeaver.");

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddFmcgConfiguration(builder.Environment);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
    var consoleConfigured = context.Configuration.GetSection("Serilog:WriteTo").GetChildren()
        .Any(sink => string.Equals(sink["Name"] ?? sink.Value, "Console", StringComparison.OrdinalIgnoreCase));
    if (!consoleConfigured) loggerConfiguration.WriteTo.Console();
});

builder.Services.AddApiServices(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddHostedService<FSCM.API.Workers.FactoryReceiptSyncWorker>();
builder.Services.AddHostedService<FSCM.API.Workers.OrderCompletionWorker>();

var app = builder.Build();

app.UseApiPipeline();

app.MapApiEndpoints();

app.Run();

public partial class Program;
