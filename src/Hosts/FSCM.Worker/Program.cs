using FSCM.BuildingBlocks.Application;
using FSCM.BuildingBlocks.Infrastructure;
using FSCM.BuildingBlocks.Infrastructure.Events;
using FSCM.BuildingBlocks.Infrastructure.Jobs;
using FSCM.Worker;

// Background host: transactional outbox dispatcher + daily jobs
// (DateTracker 01:30, Rotation 02:00 – after the date tracker, Receivable overdue 08:00; see each module's IScheduledJob).
// In production the daily jobs can run from an Azure Functions Timer Trigger calling the same IScheduledJob classes.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddFscmSharedInfrastructure(builder.Configuration);
foreach (var module in WorkerModules.Installers)
    module.Register(builder.Services, builder.Configuration);

// No HTTP user here: every action is done as "Hệ thống".
builder.Services.AddScoped<ICurrentUser, SystemCurrentUser>();

builder.Services.AddHostedService<OutboxDispatcher>();
if (builder.Configuration.GetValue("Jobs:Enabled", true))
    builder.Services.AddHostedService<DailyJobScheduler>();

builder.Build().Run();
