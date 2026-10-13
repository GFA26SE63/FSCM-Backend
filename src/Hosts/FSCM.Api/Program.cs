using System.Text.Json.Serialization;
using FSCM.Api;
using FSCM.Api.Infrastructure;
using FSCM.BuildingBlocks.Infrastructure;
using FSCM.Modules.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// 1. Shared building blocks (clock, id sequences, event bus), then every module.
builder.Services.AddFscmSharedInfrastructure(cfg);
foreach (var module in ModuleCatalog.Installers)
    module.Register(builder.Services, cfg);

// 2. Controllers live in the FSCM.Modules.*.Api assemblies.
var mvc = builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
foreach (var asm in ModuleCatalog.ApiAssemblies())
    mvc.AddApplicationPart(asm);

// 3. Auth: Microsoft Entra ID in production, a header-based dev scheme locally (see README).
builder.Services.AddFscmAuthentication(cfg);
builder.Services.AddAuthorization();

// 4. Errors as RFC 7807 ProblemDetails (BusinessRuleException -> 422, *.not_found -> 404, concurrency -> 409).
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<FscmExceptionHandler>();

builder.Services.AddFscmSwagger(cfg);
builder.Services.AddCors(o => o.AddPolicy("portal", p => p
    .WithOrigins(cfg.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" })
    .AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment() || cfg.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "FSCM API");
}
app.UseCors("portal");
app.UseAuthentication();
app.UseFscmCurrentUser();          // loads the FSCM user (role, warehouse, area, retailer) for the token subject
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

if (cfg.GetValue<bool>("Database:AutoCreate"))
    await DatabaseBootstrapper.RunAsync(app.Services, cfg, app.Logger);

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program { }
