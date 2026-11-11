using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FSCM.Domain.Authorization;

namespace FSCM.IntegrationTests.Api;

public sealed class RequestAuthorizationTests
{
    [Fact]
    public async Task Operators_and_retailers_cannot_approve_and_retailers_cannot_confirm_external_handling()
    {
        using var factory = new IdentityAuthorizationTests.IdentityApiFactory();
        using var client = factory.CreateClient();
        var id = Guid.NewGuid();
        var op = factory.AddTestUser("requests.op@test.local", SystemRoles.Operator);
        var retailer = factory.AddTestUser("requests.retailer@test.local", SystemRoles.Retailer);
        foreach (var actor in new[] { op, retailer })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",factory.CreateToken(actor));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/complaints/{id}/approve",new { note="approve" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/complaints/{id}/resolve",new { result="approve",approved=true })).StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",factory.CreateToken(retailer));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/v1/complaints/{id}/complete-handling",
            new { expectedVersion=1,actualQuantity=1,completedAt=DateTimeOffset.UtcNow,note="external",evidenceReference="proof" })).StatusCode);
    }
}
