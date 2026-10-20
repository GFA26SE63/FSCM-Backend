using System.Text;
using FSCM.API.Errors;
using FSCM.API.GraphQL;
using FSCM.API.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FSCM.API.Extensions;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicyName = "FmcgCorsPolicy";

    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));

        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddOpenApi();
        services.AddHealthChecks();
        services.AddProblemDetails();
        services.AddExceptionHandler<CatalogExceptionHandler>();
        services.AddExceptionHandler<InventoryExceptionHandler>();
        services.AddExceptionHandler<InternalTransferExceptionHandler>();
        services.AddExceptionHandler<PaymentExceptionHandler>();
        services.AddExceptionHandler<QrLabelExceptionHandler>();
        services.AddExceptionHandler<PromotionExceptionHandler>();
        services.AddExceptionHandler<RetailerExceptionHandler>();
        services.AddExceptionHandler<TerritoryExceptionHandler>();
        services.AddExceptionHandler<WarehouseConfigurationExceptionHandler>();
        services.AddExceptionHandler<IdentityExceptionHandler>();

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                var allowedOrigins = configuration
                    .GetSection($"{CorsOptions.SectionName}:AllowedOrigins")
                    .Get<string[]>() ?? [];

                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod();

                    return;
                }

                policy.AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        services.AddAuthenticationServices(configuration);
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddFscmGraphQlDataLoaders();

        services
            .AddGraphQLServer()
            .AddAuthorization()
            .AddFiltering()
            .AddSorting()
            .AddMaxExecutionDepthRule(12)
            .AddMaxAllowedFieldCycleDepthRule(2)
            .ModifyPagingOptions(options =>
            {
                options.DefaultPageSize = 20;
                options.MaxPageSize = 100;
                options.IncludeTotalCount = true;
                options.RequirePagingBoundaries = true;
            })
            .AddQueryType<SystemQuery>()
            .AddTypeExtension<ProductGraphQlExtensions>()
            .AddTypeExtension<WarehouseGraphQlExtensions>()
            .AddTypeExtension<RetailerGraphQlExtensions>()
            .AddTypeExtension<InventoryGraphQlExtensions>()
            .AddTypeExtension<GoodsReceiptGraphQlExtensions>()
            .AddTypeExtension<GoodsReceiptLineGraphQlExtensions>()
            .AddTypeExtension<QrLabelGraphQlExtensions>()
            .AddTypeExtension<InternalTransferGraphQlExtensions>()
            .AddTypeExtension<InternalTransferItemGraphQlExtensions>();

        return services;
    }

    private static IServiceCollection AddAuthenticationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(jwtOptions.Secret))
        {
            services.AddAuthentication();
            return services;
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret));

        // Allow bearer tokens over plain HTTP only in Development so the local `http` launch
        // profile (http://localhost:5246) can be used from tools like Postman/Swagger.
        var environmentName = configuration["ASPNETCORE_ENVIRONMENT"];
        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = !isDevelopment;
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrWhiteSpace(jwtOptions.Issuer),
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = !string.IsNullOrWhiteSpace(jwtOptions.Audience),
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        return services;
    }
}
