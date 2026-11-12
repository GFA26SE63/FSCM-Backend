using System.Text.Json;
using FSCM.Application.Features.Maps;
using Microsoft.Extensions.Configuration;

namespace FSCM.Infrastructure.Maps;

public sealed class VietMapProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IMapProvider
{
    public string Name => "VietMap";

    public async Task<GeocodeProviderResult> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        var apiKey = RequireApiKey();
        var client = httpClientFactory.CreateClient("VietMap");
        using var suggestionsResponse = await client.GetAsync(
            $"https://maps.vietmap.vn/api/autocomplete/v4?apikey={Uri.EscapeDataString(apiKey)}&text={Uri.EscapeDataString(address)}",
            cancellationToken);
        await EnsureSuccessAsync(suggestionsResponse, "address search", cancellationToken);

        using var suggestionsDocument = await JsonDocument.ParseAsync(
            await suggestionsResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (suggestionsDocument.RootElement.ValueKind != JsonValueKind.Array || suggestionsDocument.RootElement.GetArrayLength() == 0)
            throw MapOperationException.Provider("VietMap address search returned no result.");

        var suggestion = suggestionsDocument.RootElement[0];
        var referenceId = RequireString(suggestion, "ref_id", "address search");
        var display = suggestion.TryGetProperty("display", out var displayElement) && displayElement.ValueKind == JsonValueKind.String
            ? displayElement.GetString() ?? address
            : address;

        using var placeResponse = await client.GetAsync(
            $"https://maps.vietmap.vn/api/place/v4?apikey={Uri.EscapeDataString(apiKey)}&refid={Uri.EscapeDataString(referenceId)}",
            cancellationToken);
        await EnsureSuccessAsync(placeResponse, "place lookup", cancellationToken);

        using var placeDocument = await JsonDocument.ParseAsync(
            await placeResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var place = placeDocument.RootElement;
        return new GeocodeProviderResult(
            RequireDecimal(place, "lat", "place lookup"),
            RequireDecimal(place, "lng", "place lookup"),
            place.TryGetProperty("display", out var placeDisplay) && placeDisplay.ValueKind == JsonValueKind.String
                ? placeDisplay.GetString() ?? display
                : display,
            referenceId,
            "OK");
    }

    public async Task<RouteDistanceProviderResult> ComputeDistanceAsync(
        decimal originLatitude, decimal originLongitude,
        decimal destinationLatitude, decimal destinationLongitude,
        CancellationToken cancellationToken)
    {
        var apiKey = RequireApiKey();
        var client = httpClientFactory.CreateClient("VietMap");
        var requestUri = "https://maps.vietmap.vn/api/route/v4?apikey=" + Uri.EscapeDataString(apiKey) +
            "&vehicle=car&point=" + Coordinate(originLatitude, originLongitude) +
            "&point=" + Coordinate(destinationLatitude, destinationLongitude);
        using var response = await client.GetAsync(requestUri, cancellationToken);
        await EnsureSuccessAsync(response, "route", cancellationToken);

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = document.RootElement;
        var status = root.TryGetProperty("code", out var statusElement) ? statusElement.GetString() : null;
        if (!string.Equals(status, "OK", StringComparison.Ordinal))
            throw MapOperationException.Provider($"VietMap route failed with status '{status ?? "UNKNOWN"}'.");
        if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Array || paths.GetArrayLength() == 0)
            throw MapOperationException.Provider("VietMap route returned no route.");

        var path = paths[0];
        var distanceMeters = RequireDecimal(path, "distance", "route");
        var durationMilliseconds = RequireDecimal(path, "time", "route");
        if (distanceMeters <= 0 || durationMilliseconds <= 0 || distanceMeters > int.MaxValue)
            throw MapOperationException.Provider("VietMap route returned invalid distance or duration.");
        return new RouteDistanceProviderResult(
            (int)Math.Ceiling(distanceMeters),
            (int)Math.Ceiling(durationMilliseconds / 1_000m),
            status!);
    }

    private string RequireApiKey()
    {
        var value = configuration["VietMap:ServerApiKey"]?.Trim();
        return !string.IsNullOrWhiteSpace(value) ? value
            : throw MapOperationException.Provider("VietMap server credential is not configured.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        _ = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "VietMap rejected the server credential.",
            (System.Net.HttpStatusCode)423 => "VietMap quota is exhausted or the requested API is not enabled.",
            _ => $"VietMap {operation} returned HTTP {(int)response.StatusCode}."
        };
        throw MapOperationException.Provider(message);
    }

    private static string Coordinate(decimal latitude, decimal longitude) =>
        Uri.EscapeDataString($"{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    private static string RequireString(JsonElement element, string propertyName, string operation) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw MapOperationException.Provider($"VietMap {operation} returned an invalid response.");

    private static decimal RequireDecimal(JsonElement element, string propertyName, string operation) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetDecimal(out var value)
            ? value
            : throw MapOperationException.Provider($"VietMap {operation} returned an invalid response.");
}
