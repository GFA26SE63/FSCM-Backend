using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FSCM.API.GraphQL;
using FSCM.API.Security;
using FSCM.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace FSCM.IntegrationTests.Api;

public sealed class DynamicScreenAuthorizationTests
{
    [Fact]
    public void Every_registered_business_action_has_a_database_permission_and_no_hardcoded_role_attribute()
    {
        using var factory = new IdentityAuthorizationTests.IdentityApiFactory();
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items.OfType<ControllerActionDescriptor>();
        foreach (var action in actions)
        {
            Assert.All(action.ControllerTypeInfo.GetCustomAttributes<AuthorizeAttribute>()
                .Concat(action.MethodInfo.GetCustomAttributes<AuthorizeAttribute>()), attribute => Assert.Null(attribute.Roles));
            if (action.ControllerName is "Auth" or "Notifications" ||
                action.MethodInfo.IsDefined(typeof(AllowAnonymousAttribute))) continue;
            Assert.Contains(ApiPermissionCatalog.All,
                p => p.Key == ApiPermissionCatalog.Key(action.ControllerName, action.ActionName));
        }
    }

    [Fact]
    public async Task Revocation_and_regrant_apply_to_the_same_JWT_for_REST_and_GraphQL()
    {
        using var factory = new IdentityAuthorizationTests.IdentityApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(factory.Sales));
        foreach (var granted in new[] { true, false, true })
        {
            factory.SetPermissions(SystemRoles.Sales, granted ? ["mobile-sales-kpi", "mobile-sales-products", "api:Reports.SalesKpis", "api:Products.List"] : []);
            var rest = await client.GetAsync("/api/v1/reports/sales-kpis?fromDate=invalid");
            Assert.Equal(granted ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, rest.StatusCode);
            var response = await client.PostAsJsonAsync("/graphql", new
            {
                query = "{ entities { alias: products(first: 1) { nodes { id sku } } } }"
            });
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(!granted, json.TryGetProperty("errors", out _));
            if (!granted) Assert.Contains("AUTH_NOT_AUTHORIZED", json.ToString());
        }
    }

    [Theory]
    [InlineData(SystemRoles.Operator)]
    [InlineData(SystemRoles.WarehouseKeeper)]
    [InlineData(SystemRoles.SalesManager)]
    [InlineData(SystemRoles.Sales)]
    [InlineData(SystemRoles.Retailer)]
    public async Task Exact_API_grant_and_mapped_screen_are_required_for_every_delegated_profile(string profile)
    {
        using var factory = new IdentityAuthorizationTests.IdentityApiFactory();
        var user = factory.AddTestUser("delegation@test.invalid", profile);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(user));
        foreach (var grants in new string[][] { [], ["skus"], ["api:Products.List"], ["skus", "api:Products.List"] })
        {
            factory.SetPermissions(profile, grants);
            var response = await client.PostAsJsonAsync("/graphql", new
            { query = "{ entities { delegated: products(first: 1) { nodes { id } } } }" });
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(grants.Length != 2, json.TryGetProperty("errors", out _));
        }
        factory.SetPermissions(profile, "skus", "api:Products.Create");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/products", new { })).StatusCode);
        factory.SetPermissions(profile, "skus");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/products", new { })).StatusCode);
    }

    [Theory]
    [InlineData(SystemRoles.Operator)]
    [InlineData(SystemRoles.WarehouseKeeper)]
    [InlineData(SystemRoles.SalesManager)]
    public async Task Empty_grants_do_not_restore_default_role_access(string role)
    {
        using var factory = new IdentityAuthorizationTests.IdentityApiFactory();
        var user = factory.AddTestUser("empty-grants@test.local", role);
        factory.SetPermissions(role);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(user));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
    }

    [Fact]
    public void Every_business_controller_and_entity_field_has_a_known_screen_contract()
    {
        foreach (var controller in typeof(ScreenPermissionMap).Assembly.GetTypes()
                     .Where(t => t.IsSubclassOf(typeof(ControllerBase))))
        {
            var name = controller.Name.Replace("Controller", "");
            foreach (var role in SystemRoles.All)
            {
                var keys = ScreenPermissionMap.Rest(name, "List", role);
                if (name is "Auth" or "Notifications") Assert.Null(keys);
                else
                {
                    Assert.NotEmpty(keys!);
                    Assert.All(keys!, key => Assert.Contains(key, SystemScreenKeys.All));
                }
            }
        }
        foreach (var method in typeof(EntityQuery).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var resource = EntityScreenResources.For(method.Name);
            Assert.NotEqual("Unmapped", resource);
            if (resource == "Notifications") continue;
            var operation = EntityReadOperations.For(method.Name, resource);
            Assert.NotNull(operation);
            Assert.Contains(ApiPermissionCatalog.All, p => p.Controller == operation.Value.Controller && p.Action == operation.Value.Action);
        }
        Assert.Empty(ScreenPermissionMap.Rest("NewUnreviewedController", "List", SystemRoles.Admin)!);
    }
}
