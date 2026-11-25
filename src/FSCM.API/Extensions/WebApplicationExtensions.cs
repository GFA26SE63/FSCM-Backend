using Serilog;

namespace FSCM.API.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseSerilogRequestLogging();
        app.UseExceptionHandler();
        // Development: keep plain HTTP (http://localhost:5246) usable so the
        // mobile app on a physical phone / emulator can reach the API without
        // trusting the ASP.NET dev HTTPS certificate.
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }
        app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }

    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapControllers();
        app.MapGraphQL("/graphql");

        return app;
    }
}
