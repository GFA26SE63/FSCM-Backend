using System.Net;
using System.Text;
using FSCM.Application.Features.Maps;
using FSCM.Infrastructure.Maps;
using Microsoft.Extensions.Configuration;

namespace FSCM.IntegrationTests.Infrastructure;

public sealed class GoogleMapsProviderTests
{
    [Fact]
    public async Task Geocode_parses_google_result_without_exposing_key_in_contract()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"status":"OK","results":[{"formatted_address":"125 Nguyễn Văn Linh, Thành phố Hồ Chí Minh","place_id":"place-vn-1","geometry":{"location":{"lat":10.75,"lng":106.71}}}]}"""));
        var provider = CreateProvider(handler);

        var result = await provider.GeocodeAsync("125 Nguyễn Văn Linh", CancellationToken.None);

        Assert.Equal(10.75m, result.Latitude);
        Assert.Equal("place-vn-1", result.PlaceId);
        Assert.Contains("address=125%20Nguy%E1%BB%85n%20V%C4%83n%20Linh", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Route_matrix_requires_existing_route_and_parses_duration()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("originIndex,destinationIndex,status,condition,distanceMeters,duration",
                request.Headers.GetValues("X-Goog-FieldMask").Single());
            return Json(HttpStatusCode.OK,
                """[{"originIndex":0,"destinationIndex":0,"status":{},"condition":"ROUTE_EXISTS","distanceMeters":12500,"duration":"1800.5s"}]""");
        });
        var provider = CreateProvider(handler);

        var result = await provider.ComputeDistanceAsync(10.7m, 106.6m, 10.8m, 106.7m, CancellationToken.None);

        Assert.Equal(12_500, result.DistanceMeters);
        Assert.Equal(1_801, result.DurationSeconds);
    }

    [Fact]
    public async Task Quota_error_is_a_controlled_provider_failure()
    {
        var provider = CreateProvider(new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"status":"OVER_QUERY_LIMIT","results":[]}""")));

        var exception = await Assert.ThrowsAsync<MapOperationException>(() =>
            provider.GeocodeAsync("Thành phố Hồ Chí Minh", CancellationToken.None));

        Assert.Equal("map_provider_error", exception.Code);
        Assert.Contains("quota", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GoogleMapsProvider CreateProvider(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["GoogleMaps:ServerApiKey"] = "test-server-key" }).Build();
        return new GoogleMapsProvider(new StubClientFactory(new HttpClient(handler)), configuration);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class StubClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response(request));
        }
    }
}
