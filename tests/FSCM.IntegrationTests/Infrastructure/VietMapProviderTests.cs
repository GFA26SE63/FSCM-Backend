using System.Net;
using System.Text;
using FSCM.Application.Features.Maps;
using FSCM.Infrastructure.Maps;
using Microsoft.Extensions.Configuration;

namespace FSCM.IntegrationTests.Infrastructure;

public sealed class VietMapProviderTests
{
    [Fact]
    public async Task Geocode_uses_autocomplete_and_place_without_exposing_key_in_contract()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/autocomplete/v4" => Json(HttpStatusCode.OK,
                """[{"ref_id":"geocode:place-vn-1","display":"125 Nguyễn Văn Linh, Thành phố Hồ Chí Minh"}]"""),
            "/api/place/v4" => Json(HttpStatusCode.OK,
                """{"display":"125 Nguyễn Văn Linh, Thành phố Hồ Chí Minh","lat":10.75,"lng":106.71}"""),
            _ => throw new InvalidOperationException("Unexpected VietMap endpoint.")
        });
        var provider = CreateProvider(handler);

        var result = await provider.GeocodeAsync("125 Nguyễn Văn Linh", CancellationToken.None);

        Assert.Equal(10.75m, result.Latitude);
        Assert.Equal(106.71m, result.Longitude);
        Assert.Equal("geocode:place-vn-1", result.PlaceId);
        Assert.Equal("VietMap", provider.Name);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("text=125%20Nguy%E1%BB%85n%20V%C4%83n%20Linh", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("refid=geocode%3Aplace-vn-1", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task Route_parses_distance_and_millisecond_duration_for_a_car()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Contains("vehicle=car", request.RequestUri!.Query);
            Assert.Contains("point=10.7%2C106.6", request.RequestUri.Query);
            return Json(HttpStatusCode.OK,
                """{"code":"OK","paths":[{"distance":12500.1,"time":1800500}]}""");
        });
        var provider = CreateProvider(handler);

        var result = await provider.ComputeDistanceAsync(10.7m, 106.6m, 10.8m, 106.7m, CancellationToken.None);

        Assert.Equal(12_501, result.DistanceMeters);
        Assert.Equal(1_801, result.DurationSeconds);
        Assert.Equal("OK", result.ProviderStatus);
    }

    [Fact]
    public async Task Quota_error_is_a_controlled_provider_failure()
    {
        var provider = CreateProvider(new StubHandler(_ => Json((HttpStatusCode)423, """{"message":"quota exceeded"}""")));

        var exception = await Assert.ThrowsAsync<MapOperationException>(() =>
            provider.GeocodeAsync("Thành phố Hồ Chí Minh", CancellationToken.None));

        Assert.Equal("map_provider_error", exception.Code);
        Assert.Contains("quota", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static VietMapProvider CreateProvider(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["VietMap:ServerApiKey"] = "test-server-key" }).Build();
        return new VietMapProvider(new StubClientFactory(new HttpClient(handler)), configuration);
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
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response(request));
        }
    }
}
