namespace PAQTERIA.Server.Models;

public static class RouteRequestValidation
{
    public static string? Validate(RouteAssignmentRequest request)
    {
        if (request.IdRepartidor is null or <= 0) return "Selecciona un repartidor válido.";
        if (request.IdUnidad is null or <= 0) return "Selecciona una unidad válida.";
        if (request.FechaTurno is null) return "La fecha del turno es obligatoria.";
        if (request.FechaTurno < DateOnly.FromDateTime(DateTime.Today)) return "La fecha del turno no puede estar en el pasado.";
        if (request.TiempoEstimadoHoras is null or <= 0) return "El tiempo estimado debe ser mayor que cero.";
        if (request.IdPaquetes is null || request.IdPaquetes.Count == 0) return "Selecciona al menos un paquete.";
        if (request.IdPaquetes.Any(id => id <= 0)) return "La selección de paquetes no es válida.";
        if (request.IdPaquetes.Distinct().Count() != request.IdPaquetes.Count) return "No se puede repetir un paquete en la misma ruta.";
        if (request.OrigenLatitud is null or < -90 or > 90 || request.OrigenLongitud is null or < -180 or > 180)
            return "No se recibió una ubicación válida del repartidor.";
        return null;
    }
}
