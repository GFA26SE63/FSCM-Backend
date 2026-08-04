using Serilog;

namespace FMCG.API.Extensions;

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
        app.UseHttpsRedirection();
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
