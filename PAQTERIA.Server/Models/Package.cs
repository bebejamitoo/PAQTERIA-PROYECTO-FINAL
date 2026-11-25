namespace PAQTERIA.Server.Models;

public sealed class Package
{
    public int IdPaquete { get; init; }
    public required string Folio { get; init; }
    public int IdCentroOrigen { get; init; }
    public string DireccionOrigen { get; init; } = string.Empty;
    public string DireccionDestino { get; init; } = string.Empty;
    public string? CoordenadasDestino { get; init; }
    public decimal PesoKg { get; init; }
    public string TamanoEtiqueta { get; init; } = string.Empty;
    public bool EsPrioritario { get; init; }
    public bool EsFragil { get; init; }
    public required string EstadoActual { get; init; }
    public DateTime FechaCreacion { get; init; }
}
