using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PAQTERIA.Server.Data;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[Route("api/mobile/routes")]
[Authorize(Roles = "Repartidor")]
public sealed class MobileRoutesController(RouteDb routeDb) : ControllerBase
{
    [HttpGet("active")]
    public async Task<ActionResult<MobileActiveRouteResponse>> GetActiveRoute(
        CancellationToken cancellationToken)
    {
        var idRepartidor = GetCurrentUserId();
        if (idRepartidor is null) return Unauthorized();

        var route = await routeDb.GetActiveRouteAsync(idRepartidor.Value, cancellationToken);
        return route is null
            ? NotFound(new { message = "No tienes un turno asignado para hoy." })
            : Ok(route);
    }

    [HttpPost("{idTurno:int}/start")]
    public async Task<IActionResult> StartRoute(
        int idTurno,
        CancellationToken cancellationToken)
    {
        var idRepartidor = GetCurrentUserId();
        if (idRepartidor is null) return Unauthorized();

        var started = await routeDb.StartRouteAsync(idTurno, idRepartidor.Value, cancellationToken);
        return started
            ? Ok(new { idTurno, estado = "En curso", message = "La ruta se inició correctamente." })
            : Conflict(new { message = "El turno no existe, no te pertenece o ya no se puede iniciar." });
    }

    [HttpPost("{idTurno:int}/location")]
    public async Task<IActionResult> SaveLocation(
        int idTurno,
        [FromBody] MobileLocationRequest request,
        CancellationToken cancellationToken)
    {
        var idRepartidor = GetCurrentUserId();
        if (idRepartidor is null) return Unauthorized();

        var validationError = ValidateLocation(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        var saved = await routeDb.SaveLocationAsync(
            idTurno, idRepartidor.Value, request with
            {
                FechaHoraUtc = NormalizeUtc(request.FechaHoraUtc!.Value)
            }, cancellationToken);

        return saved
            ? Ok(new { idTurno, message = "Ubicación recibida." })
            : Conflict(new { message = "La ruta no está iniciada o no pertenece al repartidor." });
    }

    private int? GetCurrentUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario)
            ? idUsuario
            : null;

    private static string? ValidateLocation(MobileLocationRequest request)
    {
        if (request.Latitud is null or < -90 or > 90)
            return "La latitud debe estar entre -90 y 90.";
        if (request.Longitud is null or < -180 or > 180)
            return "La longitud debe estar entre -180 y 180.";
        if (request.FechaHoraUtc is null)
            return "La fecha y hora de la ubicación son obligatorias.";
        if (request.PrecisionMetros is < 0)
            return "La precisión no puede ser negativa.";
        if (request.VelocidadKmh is < 0)
            return "La velocidad no puede ser negativa.";
        if (request.RumboGrados is < 0 or > 360)
            return "El rumbo debe estar entre 0 y 360 grados.";

        var timestamp = NormalizeUtc(request.FechaHoraUtc.Value);
        var now = DateTime.UtcNow;
        if (timestamp > now.AddMinutes(5))
            return "La fecha de ubicación no puede estar en el futuro.";
        if (timestamp < now.AddHours(-24))
            return "La ubicación es demasiado antigua.";

        return null;
    }

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
}
