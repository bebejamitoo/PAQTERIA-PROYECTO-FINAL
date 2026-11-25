namespace PAQTERIA.Server.Models;

using System.Globalization;

public sealed record CreatePackageRequest(
    string? Folio,
    int? IdCentroOrigen,
    string? DireccionOrigen,
    string? DireccionDestino,
    string? CoordenadasDestino,
    decimal? PesoKg,
    string? TamanoEtiqueta,
    bool EsPrioritario,
    bool EsFragil);

public sealed record PackageResponse(
    int IdPaquete,
    string Folio,
    int IdCentroOrigen,
    string DireccionOrigen,
    string DireccionDestino,
    string? CoordenadasDestino,
    decimal PesoKg,
    string TamanoEtiqueta,
    bool EsPrioritario,
    bool EsFragil,
    string EstadoActual,
    DateTime FechaCreacion);

public sealed record MobilePackageResponse(
    int IdPaquete,
    string Folio,
    string DireccionDestino,
    bool EsPrioritario,
    bool EsFragil,
    string EstadoActual,
    string? CoordenadasDestino,
    int? OrdenSecuencia,
    string? EstadoParada);

public sealed record ClientPackageResponse(
    int IdPaquete,
    string Folio,
    string DireccionDestino,
    string? CoordenadasDestino,
    bool EsPrioritario,
    bool EsFragil,
    string EstadoActual);

public sealed record PublicTrackingResponse(
    string Folio,
    string EstadoActual,
    string? CoordenadasDestino,
    string? ColoniaOCiudad);

public static class PackageRequestValidation
{
    public static string? Validate(CreatePackageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Folio)) return "El folio es obligatorio.";
        if (request.IdCentroOrigen is null or <= 0) return "El centro de origen debe ser un identificador válido.";
        if (string.IsNullOrWhiteSpace(request.DireccionOrigen)) return "La dirección de origen es obligatoria.";
        if (string.IsNullOrWhiteSpace(request.DireccionDestino)) return "La dirección de destino es obligatoria.";
        if (string.IsNullOrWhiteSpace(request.CoordenadasDestino)) return "Las coordenadas de destino son obligatorias.";

        var coordinateParts = request.CoordenadasDestino.Split(
            ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (coordinateParts.Length != 2
            || !double.TryParse(coordinateParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(coordinateParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || latitude is < 14 or > 33.5 || longitude is < -118.5 or > -86.5)
        {
            return "Las coordenadas deben estar en México y usar el formato latitud, longitud; por ejemplo: 29.0729,-110.9559.";
        }

        if (request.PesoKg is null or <= 0) return "El peso debe ser mayor que cero.";
        if (string.IsNullOrWhiteSpace(request.TamanoEtiqueta)) return "El tamaño de etiqueta es obligatorio.";
        return null;
    }
}
