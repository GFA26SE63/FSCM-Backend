using System.Text;
using System.Security.Cryptography;
using FSCM.API.Errors;
using FSCM.API.GraphQL;
using FSCM.API.Options;
using FSCM.API.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using HotChocolate.Types.Descriptors;

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
        services.Configure<GoogleOptions>(configuration.GetSection(GoogleOptions.SectionName));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.SectionName));

        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        services.AddOpenApi();
        services.AddHealthChecks();
        services.AddProblemDetails();
        services.AddExceptionHandler<AllocationRunExceptionHandler>();
        services.AddExceptionHandler<CatalogExceptionHandler>();
        services.AddExceptionHandler<DeliveryExceptionHandler>();
        services.AddExceptionHandler<ComplaintExceptionHandler>();
        services.AddExceptionHandler<ShipmentExceptionHandler>();
        services.AddExceptionHandler<ReportExceptionHandler>();
        services.AddExceptionHandler<NotificationExceptionHandler>();
        services.AddExceptionHandler<InventoryExceptionHandler>();
        services.AddExceptionHandler<InventoryLockExceptionHandler>();
        services.AddExceptionHandler<InternalTransferExceptionHandler>();
        services.AddExceptionHandler<LoyaltyExceptionHandler>();
        services.AddExceptionHandler<ManualPaymentExceptionHandler>();
        services.AddExceptionHandler<MapExceptionHandler>();
        services.AddExceptionHandler<OrderExceptionHandler>();
        services.AddExceptionHandler<PaymentExceptionHandler>();
        services.AddExceptionHandler<PickingExceptionHandler>();
        services.AddExceptionHandler<QrLabelExceptionHandler>();
        services.AddExceptionHandler<PromotionExceptionHandler>();
        services.AddExceptionHandler<RetailerExceptionHandler>();
        services.AddExceptionHandler<RefundExceptionHandler>();
        services.AddExceptionHandler<QuoteExceptionHandler>();
        services.AddExceptionHandler<TerritoryExceptionHandler>();
        services.AddExceptionHandler<ShelfLifeDiscountExceptionHandler>();
        services.AddExceptionHandler<TransitTableExceptionHandler>();
        services.AddExceptionHandler<DeliveryEtaExceptionHandler>();
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
        services.AddScoped<IClaimsTransformation, ActiveAccountClaimsTransformation>();
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy(AuthorizationPolicies.GoogleExchange, policy =>
            {
                policy.AddAuthenticationSchemes(AuthenticationSchemes.Google);
                policy.RequireAuthenticatedUser();
            });
        });
        services.AddHttpContextAccessor();
        var graphQl = services
            .AddGraphQLServer()
            .AddAuthorization()
            .AddConvention<INamingConventions, EntityNamingConventions>()
            .AddProjections()
            .AddFiltering()
            .AddSorting()
            .AddMaxExecutionDepthRule(12)
            .AddMaxAllowedFieldCycleDepthRule(2)
            .ModifyPagingOptions(options =>
            {
                options.DefaultPageSize = 20;
                options.MaxPageSize = int.MaxValue;
                options.IncludeTotalCount = true;
                options.RequirePagingBoundaries = false;
            })
            .AddQueryType<SystemQuery>();

        foreach (var method in typeof(EntityQuery).GetMethods(
                     System.Reflection.BindingFlags.Instance |
                     System.Reflection.BindingFlags.Public |
                     System.Reflection.BindingFlags.DeclaredOnly))
        {
            if (!method.ReturnType.IsGenericType ||
                method.ReturnType.GetGenericTypeDefinition() != typeof(IQueryable<>))
            {
                continue;
            }

            var entityType = method.ReturnType.GetGenericArguments()[0];
            graphQl.AddType(typeof(DomainEntityObjectType<>).MakeGenericType(entityType));
        }

        return services;
    }

    private static IServiceCollection AddAuthenticationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = configuration
            .GetSection(JwtOptions.SectionName)
            .Get<JwtOptions>() ?? new JwtOptions();
        var googleOptions = configuration
            .GetSection(GoogleOptions.SectionName)
            .Get<GoogleOptions>() ?? new GoogleOptions();
        var localKeyBytes = Encoding.UTF8.GetBytes(jwtOptions.Secret);
        var signingKey = new SymmetricSecurityKey(
            localKeyBytes.Length >= 32 ? localKeyBytes : RandomNumberGenerator.GetBytes(32));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = true;
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
            })
            .AddJwtBearer(AuthenticationSchemes.Google, options =>
            {
                var clientIds = googleOptions.ClientIds
                    .Where(clientId => !string.IsNullOrWhiteSpace(clientId))
                    .Select(clientId => clientId.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var configured = clientIds.Length > 0;
                if (configured)
                {
                    options.Authority = "https://accounts.google.com";
                }
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = configured
                        ? ["https://accounts.google.com", "accounts.google.com"]
                        : ["__google_not_configured__"],
                    ValidateAudience = true,
                    ValidAudiences = configured ? clientIds : ["__google_not_configured__"],
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    NameClaimType = "name",
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        return services;
    }
}
