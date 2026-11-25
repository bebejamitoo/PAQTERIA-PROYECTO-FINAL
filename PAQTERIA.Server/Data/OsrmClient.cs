using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Data;

public sealed class OsrmClient(HttpClient httpClient, IConfiguration configuration)
{
    private readonly string _baseUrl = (configuration["Osrm:BaseUrl"] ?? "https://router.project-osrm.org").TrimEnd('/');

    public async Task<RouteCalculation> CalculateAsync(
        IReadOnlyList<RouteCoordinate> coordinates,
        CancellationToken cancellationToken = default)
    {
        if (coordinates.Count < 2)
        {
            throw new InvalidOperationException("Se requieren el origen y al menos una parada para calcular la ruta.");
        }

        var coordinateText = string.Join(';', coordinates.Select(point =>
            $"{point.Longitude.ToString(CultureInfo.InvariantCulture)},{point.Latitude.ToString(CultureInfo.InvariantCulture)}"));
        var url = $"{_baseUrl}/route/v1/driving/{coordinateText}?overview=full&geometries=geojson&steps=false";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return CreateFallbackRoute(coordinates);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("code", out var code)
                || !string.Equals(code.GetString(), "Ok", StringComparison.OrdinalIgnoreCase))
            {
                return CreateFallbackRoute(coordinates);
            }

            var route = root.GetProperty("routes")[0];
            var geometry = route.GetProperty("geometry").GetRawText();
            return new RouteCalculation(
                geometry,
                route.GetProperty("distance").GetDouble(),
                route.GetProperty("duration").GetDouble());
        }
        catch (HttpRequestException)
        {
            return CreateFallbackRoute(coordinates);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("El servicio de rutas devolvió una respuesta inválida.");
        }
    }

    private static RouteCalculation CreateFallbackRoute(IReadOnlyList<RouteCoordinate> coordinates)
    {
        // Routing is an optional enhancement: a public OSRM outage/403 must not
        // prevent assigning the packages to the selected driver.
        var geometry = JsonSerializer.Serialize(new
        {
            type = "LineString",
            coordinates = coordinates.Select(point => new[] { point.Longitude, point.Latitude })
        });

        var distanceMeters = 0d;
        for (var index = 1; index < coordinates.Count; index++)
        {
            distanceMeters += HaversineMeters(coordinates[index - 1], coordinates[index]) * 1.3;
        }

        // Conservative estimate for an approximate road distance at 35 km/h.
        var durationSeconds = distanceMeters / (35_000d / 3_600d);
        return new RouteCalculation(geometry, distanceMeters, durationSeconds);
    }

    private static double HaversineMeters(RouteCoordinate first, RouteCoordinate second)
    {
        const double earthRadiusMeters = 6_371_000;
        var latitudeDelta = DegreesToRadians(second.Latitude - first.Latitude);
        var longitudeDelta = DegreesToRadians(second.Longitude - first.Longitude);
        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
            + Math.Cos(DegreesToRadians(first.Latitude))
            * Math.Cos(DegreesToRadians(second.Latitude))
            * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
