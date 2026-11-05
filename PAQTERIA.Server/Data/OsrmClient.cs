using System.Globalization;
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
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!string.Equals(root.GetProperty("code").GetString(), "Ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("OSRM no encontró una ruta para las coordenadas proporcionadas.");
        }

        var route = root.GetProperty("routes")[0];
        var geometry = route.GetProperty("geometry").GetRawText();
        return new RouteCalculation(
            geometry,
            route.GetProperty("distance").GetDouble(),
            route.GetProperty("duration").GetDouble());
    }
}
