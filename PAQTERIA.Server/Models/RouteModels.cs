namespace PAQTERIA.Server.Models;

public sealed record RouteAssignmentRequest(
    int? IdRepartidor,
    int? IdUnidad,
    DateOnly? FechaTurno,
    decimal? TiempoEstimadoHoras,
    IReadOnlyList<int>? IdPaquetes,
    double? OrigenLatitud,
    double? OrigenLongitud);

public sealed record RouteOptionsResponse(
    IReadOnlyList<RouteDriverOption> Repartidores,
    IReadOnlyList<RouteUnitOption> Unidades,
    IReadOnlyList<RoutePackageOption> PaquetesDisponibles);

public sealed record RouteDriverOption(int IdRepartidor, string Nombre, string Email);

public sealed record RouteUnitOption(int IdUnidad, string CodigoUnidad, string Placas);

public sealed record RoutePackageOption(
    int IdPaquete,
    string Folio,
    string DireccionDestino,
    bool EsPrioritario,
    decimal PesoKg,
    double Latitud,
    double Longitud);

public sealed record RouteMonitoringResponse(
    IReadOnlyList<RouteMonitoringItem> Turnos,
    int PaquetesEnRuta,
    int ParadasPendientes,
    int IncidenciasAbiertas);

public sealed record RouteMonitoringItem(
    int IdTurno,
    DateOnly FechaTurno,
    string Repartidor,
    string Unidad,
    string EstadoTurno,
    int TotalPaquetes,
    int ParadasPendientes,
    int Incidencias,
    IReadOnlyList<RouteStopItem> Paradas,
    string? Geometria,
    double? DistanciaMetros,
    double? DuracionSegundos);

public sealed record RouteStopItem(
    int IdParada,
    int IdPaquete,
    string Folio,
    int OrdenSecuencia,
    string EstadoParada,
    string DireccionDestino,
    string EstadoPaquete,
    string UltimoEvento);

public sealed record RouteAssignmentResponse(int IdTurno, int TotalPaquetes, string Message);

public sealed record MobileActiveRouteResponse(
    int IdTurno,
    DateOnly FechaTurno,
    string EstadoTurno,
    int TotalPaquetes,
    string? Geometria,
    double? DistanciaMetros,
    double? DuracionSegundos,
    MobileLastLocationResponse? UltimaUbicacion);

public sealed record MobileLastLocationResponse(
    double Latitud,
    double Longitud,
    double? PrecisionMetros,
    double? VelocidadKmh,
    double? RumboGrados,
    DateTime FechaHoraUtc);

public sealed record MobileLocationRequest(
    double? Latitud,
    double? Longitud,
    double? PrecisionMetros,
    double? VelocidadKmh,
    double? RumboGrados,
    DateTime? FechaHoraUtc);

public sealed record RouteCoordinate(double Latitude, double Longitude);

public sealed record RouteCalculation(string Geometry, double DistanceMeters, double DurationSeconds);
