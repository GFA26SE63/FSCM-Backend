using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FSCM.IntegrationTests.GraphQL;

public sealed class GraphQlReadLayerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public GraphQlReadLayerTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development")).CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Status_Query_Remains_Available()
    {
        using var response = await PostGraphQlAsync("query { status }");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("FSCM API is running",
            document.RootElement.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Protected_Read_Query_Rejects_Anonymous_User()
    {
        using var response = await PostGraphQlAsync(
            "query { products(first: 1) { nodes { id name } } }");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = document.RootElement.GetProperty("errors")[0];
        Assert.Equal("AUTH_NOT_AUTHENTICATED",
            error.GetProperty("extensions").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Business_Collections_Expose_Bounded_Paging_Filtering_And_Sorting()
    {
        const string query = """
            query {
              __type(name: "SystemQuery") {
                fields { name args { name defaultValue } }
              }
            }
            """;

        using var response = await PostGraphQlAsync(query);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var fields = document.RootElement.GetProperty("data").GetProperty("__type")
            .GetProperty("fields").EnumerateArray().ToArray();

        foreach (var fieldName in new[] { "products", "retailers", "inventory", "internalTransfers" })
        {
            var field = fields.Single(item => item.GetProperty("name").GetString() == fieldName);
            var arguments = field.GetProperty("args").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString()).ToHashSet();
            Assert.Contains("first", arguments);
            Assert.Contains("after", arguments);
            Assert.Contains("where", arguments);
            Assert.Contains("order", arguments);
        }

        var inventory = fields.Single(item => item.GetProperty("name").GetString() == "inventory");
        var includeExpired = inventory.GetProperty("args").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "includeExpired");
        Assert.Equal("false", includeExpired.GetProperty("defaultValue").GetString());
    }

    [Fact]
    public async Task Product_Type_Exposes_Useful_Relationships_Without_Sensitive_Identity_Fields()
    {
        const string query = """
            query {
              product: __type(name: "ProductResult") { fields { name } }
              user: __type(name: "AuthenticatedUser") { fields { name } }
            }
            """;

        using var response = await PostGraphQlAsync(query);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        var productFields = data.GetProperty("product").GetProperty("fields").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()).ToHashSet();
        Assert.Contains("category", productFields);
        Assert.Contains("currentPrice", productFields);
        Assert.Contains("prices", productFields);
        Assert.Contains("inventory", productFields);
        Assert.Contains("mlorConfigurations", productFields);

        var userFields = data.GetProperty("user").GetProperty("fields").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()).ToHashSet();
        Assert.DoesNotContain("passwordHash", userFields);
        Assert.DoesNotContain("refreshToken", userFields);
        Assert.DoesNotContain("activationToken", userFields);
        Assert.DoesNotContain("passwordResetToken", userFields);
    }

    [Theory]
    [InlineData("/api/v1/products")]
    [InlineData("/api/v1/warehouses")]
    [InlineData("/api/v1/retailers")]
    [InlineData("/api/v1/inventory")]
    public async Task Representative_Rest_Get_Routes_Remain_Mapped(string path)
    {
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private Task<HttpResponseMessage> PostGraphQlAsync(string query) =>
        client.PostAsJsonAsync("/graphql", new { query });
}
