namespace PAQTERIA.Server.Models;

public sealed class DeliveryIncident
{
    public int IdIncidencia { get; init; }
    public int IdPaquete { get; init; }
    public int? IdRepartidor { get; init; }
    public required string TipoIncidencia { get; init; }
    public string? Comentario { get; init; }
    public string? UrlFotoReporte { get; init; }
    public DateTime? FechaHora { get; init; }
    public required string Folio { get; init; }
    public string? Repartidor { get; init; }
}

public sealed record CreateIncidentRequest(
    int? IdPaquete,
    int? IdRepartidor,
    string? TipoIncidencia,
    string? Comentario,
    string? UrlFotoReporte);

public sealed record CreateClientIncidentRequest(
    int? IdPaquete,
    string? TipoIncidencia,
    string? Comentario);

public sealed record IncidentResponse(
    int IdIncidencia,
    int IdPaquete,
    int? IdRepartidor,
    string TipoIncidencia,
    string? Comentario,
    string? UrlFotoReporte,
    DateTime? FechaHora,
    string Folio,
    string? Repartidor);

public sealed record IncidentPackageOption(int IdPaquete, string Folio, string DireccionDestino);

public sealed record IncidentDriverOption(int IdRepartidor, string Nombre);

public sealed record IncidentOptionsResponse(
    IReadOnlyList<IncidentPackageOption> Paquetes,
    IReadOnlyList<IncidentDriverOption> Repartidores);
