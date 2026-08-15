using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Wms.Modules.Fulfillment.Application.Optimization;

public sealed record RouteQueryPoint(decimal Latitude, decimal Longitude);

public sealed record RouteInfo(decimal DistanceKm, decimal DurationMinutes, string Source);

public interface IRouteProvider
{
    Task<RouteInfo> GetRouteAsync(RouteQueryPoint origin, RouteQueryPoint destination, CancellationToken cancellationToken);
}

/// <summary>
/// Haversine — offline basit fallback ve pre-filter. Mesafe km, süre ~60 km/saat varsayımı.
/// </summary>
public sealed class HaversineRouteProvider : IRouteProvider
{
    public Task<RouteInfo> GetRouteAsync(RouteQueryPoint origin, RouteQueryPoint destination, CancellationToken cancellationToken)
    {
        var distanceKm = HaversineKm(origin, destination);
        var durationMinutes = distanceKm * 60m / 60m; // 60 km/saat
        return Task.FromResult(new RouteInfo(distanceKm, durationMinutes, "HAVERSINE"));
    }

    internal static decimal HaversineKm(RouteQueryPoint a, RouteQueryPoint b)
    {
        const decimal earthRadiusKm = 6371.0m;
        var lat1 = ToRadians(a.Latitude);
        var lat2 = ToRadians(b.Latitude);
        var dLat = ToRadians(b.Latitude - a.Latitude);
        var dLon = ToRadians(b.Longitude - a.Longitude);

        var sinLat = Sin(dLat / 2m);
        var sinLon = Sin(dLon / 2m);
        var h = sinLat * sinLat + Cos(lat1) * Cos(lat2) * sinLon * sinLon;

        return 2m * earthRadiusKm * Asin(Sqrt(h));
    }

    private static decimal ToRadians(decimal degrees) => degrees * 3.141592653589793238462643383279502884m / 180m;

    private static decimal Sin(decimal x) => (decimal)Math.Sin((double)x);

    private static decimal Cos(decimal x) => (decimal)Math.Cos((double)x);

    private static decimal Sqrt(decimal x) => (decimal)Math.Sqrt((double)x);

    private static decimal Asin(decimal x) => (decimal)Math.Asin((double)x);
}

/// <summary>
/// OSRM (self-hosted) — gerçek yol mesafesi. Unavailable → RouteUnavailableException fırlatır;
/// optimizer bunu Haversine fallback'e çevirir (sessiz fallback YOK — source alanına yazılır).
/// </summary>
public sealed class OsrmRouteProvider(HttpClient httpClient) : IRouteProvider
{
    public async Task<RouteInfo> GetRouteAsync(RouteQueryPoint origin, RouteQueryPoint destination, CancellationToken cancellationToken)
    {
        var coordinates = $"{origin.Longitude.ToString(CultureInfo.InvariantCulture)},{origin.Latitude.ToString(CultureInfo.InvariantCulture)};{destination.Longitude.ToString(CultureInfo.InvariantCulture)},{destination.Latitude.ToString(CultureInfo.InvariantCulture)}";

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(
                $"/route/v1/driving/{coordinates}?overview=false",
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new RouteUnavailableException($"OSRM erişilemedi: {exception.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RouteUnavailableException("OSRM zaman aşımına uğradı.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new RouteUnavailableException($"OSRM hata döndü: {(int)response.StatusCode}");
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
        {
            throw new RouteUnavailableException("OSRM yanıtında route bulunamadı.");
        }

        var route = routes[0];
        var distanceMeters = route.GetProperty("distance").GetDecimal();
        var durationSeconds = route.GetProperty("duration").GetDecimal();

        return new RouteInfo(
            distanceMeters / 1000m,
            durationSeconds / 60m,
            "OSRM");
    }
}

public sealed class RouteUnavailableException : Exception
{
    public RouteUnavailableException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Deterministik in-memory cache: (origin, destination, provider/version) → RouteInfo.
/// </summary>
public sealed class CachingRouteProvider(IRouteProvider inner, string version = "v1") : IRouteProvider
{
    private readonly Dictionary<(decimal, decimal, decimal, decimal), RouteInfo> _cache = [];

    public async Task<RouteInfo> GetRouteAsync(RouteQueryPoint origin, RouteQueryPoint destination, CancellationToken cancellationToken)
    {
        var key = (origin.Latitude, origin.Longitude, destination.Latitude, destination.Longitude);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var route = await inner.GetRouteAsync(origin, destination, cancellationToken);
        _cache[key] = route;
        return route;
    }

    public string Version => version;
}
