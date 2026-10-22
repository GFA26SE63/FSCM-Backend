using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using FSCM.Application.Features.Maps;
using Microsoft.Extensions.Configuration;

namespace FSCM.Infrastructure.Maps;

public sealed class GoogleMapsProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IMapProvider
{
    public string Name => "GoogleMaps";

    public async Task<GeocodeProviderResult> GeocodeAsync(string address, CancellationToken cancellationToken)
    {
        var apiKey = RequireApiKey();
        var client = httpClientFactory.CreateClient("GoogleMaps");
        using var response = await client.GetAsync(
            $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(address)}&region=vn&language=vi&key={Uri.EscapeDataString(apiKey)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw MapOperationException.Provider($"Google Maps geocoding returned HTTP {(int)response.StatusCode}.");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = document.RootElement;
        var status = root.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        if (!string.Equals(status, "OK", StringComparison.Ordinal))
            throw MapOperationException.Provider(MapStatusMessage("geocoding", status));
        var results = root.GetProperty("results");
        if (results.GetArrayLength() == 0)
            throw MapOperationException.Provider("Google Maps could not resolve the supplied address.");
        var first = results[0];
        var coordinates = first.GetProperty("geometry").GetProperty("location");
        return new GeocodeProviderResult(
            coordinates.GetProperty("lat").GetDecimal(),
            coordinates.GetProperty("lng").GetDecimal(),
            first.GetProperty("formatted_address").GetString() ?? address,
            first.GetProperty("place_id").GetString() ?? string.Empty,
            status ?? "UNKNOWN");
    }

    public async Task<RouteDistanceProviderResult> ComputeDistanceAsync(
        decimal originLatitude, decimal originLongitude,
        decimal destinationLatitude, decimal destinationLongitude,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            origins = new[] { new { waypoint = Waypoint(originLatitude, originLongitude) } },
            destinations = new[] { new { waypoint = Waypoint(destinationLatitude, destinationLongitude) } },
            travelMode = "DRIVE",
            routingPreference = "TRAFFIC_AWARE",
            languageCode = "vi-VN",
            units = "METRIC"
        };
        var client = httpClientFactory.CreateClient("GoogleMaps");
        using var message = new HttpRequestMessage(HttpMethod.Post,
            "https://routes.googleapis.com/distanceMatrix/v2:computeRouteMatrix")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-Goog-Api-Key", RequireApiKey());
        message.Headers.Add("X-Goog-FieldMask", "originIndex,destinationIndex,status,condition,distanceMeters,duration");
        using var response = await client.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw MapOperationException.Provider($"Google Maps Routes returned HTTP {(int)response.StatusCode}.");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            throw MapOperationException.Provider("Google Maps Routes returned no route.");
        var element = document.RootElement[0];
        var condition = element.TryGetProperty("condition", out var conditionElement)
            ? conditionElement.GetString() : null;
        if (!string.Equals(condition, "ROUTE_EXISTS", StringComparison.Ordinal) ||
            !element.TryGetProperty("distanceMeters", out var distanceElement) ||
            !element.TryGetProperty("duration", out var durationElement))
            throw MapOperationException.Provider("Google Maps Routes could not calculate this route.");
        var durationText = durationElement.GetString() ?? string.Empty;
        if (!durationText.EndsWith('s') ||
            !decimal.TryParse(durationText[..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var seconds))
            throw MapOperationException.Provider("Google Maps Routes returned an invalid duration.");
        return new RouteDistanceProviderResult(distanceElement.GetInt32(),
            (int)Math.Ceiling(seconds), condition ?? "UNKNOWN");
    }

    private string RequireApiKey()
    {
        var value = configuration["GoogleMaps:ServerApiKey"]?.Trim();
        return !string.IsNullOrWhiteSpace(value) ? value
            : throw MapOperationException.Provider("Google Maps server credential is not configured.");
    }

    private static object Waypoint(decimal latitude, decimal longitude) =>
        new { location = new { latLng = new { latitude, longitude } } };

    private static string MapStatusMessage(string operation, string? status) => status switch
    {
        "ZERO_RESULTS" => $"Google Maps {operation} returned no result.",
        "OVER_QUERY_LIMIT" => "Google Maps quota was exceeded.",
        "REQUEST_DENIED" => "Google Maps rejected the server credential or request.",
        "INVALID_REQUEST" => $"Google Maps {operation} request was invalid.",
        _ => $"Google Maps {operation} failed with status '{status ?? "UNKNOWN"}'."
    };
}
