namespace FSCM.Application.Features.Maps;

public interface IMapProvider
{
    string Name { get; }
    Task<GeocodeProviderResult> GeocodeAsync(string address, CancellationToken cancellationToken);
    Task<RouteDistanceProviderResult> ComputeDistanceAsync(
        decimal originLatitude,
        decimal originLongitude,
        decimal destinationLatitude,
        decimal destinationLongitude,
        CancellationToken cancellationToken);
}
