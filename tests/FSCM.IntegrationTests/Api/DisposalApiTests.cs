using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace FSCM.IntegrationTests.Api;

public sealed class DisposalApiTests
{
    [Fact]
    public async Task Disposal_routes_require_authentication_and_reject_invalid_commands_before_accessing_stock()
    {
        using var factory = new IdentityAuthorizationTests.IdentityApiFactory();
        using var client = factory.CreateClient();
        foreach (var route in new[] { "/api/v1/disposals", "/api/v1/disposals/candidates", "/api/v1/disposals/unknown" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(factory.Sales));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/disposals/candidates")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(factory.Admin));
        var invalid = new { batchCodes = Array.Empty<string>(), method = "Tiêu hủy tại kho", operationId = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/disposals", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/disposals",
            new { batchCodes = new[] { "A" }, method = "invalid", operationId = Guid.NewGuid() })).StatusCode);
    }
}
