using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PAQTERIA.Server.Data;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[Route("api/client/reportes")]
[Authorize(Roles = "Cliente")]
public sealed class ClientIncidentsController(IncidentDb incidentDb) : ControllerBase
{
    private static readonly HashSet<string> AllowedTypes =
        new(StringComparer.Ordinal) { "no_llegado", "direccion", "danado", "otro" };

    [HttpPost]
    public async Task<ActionResult<IncidentResponse>> CreateReport(
        [FromBody] CreateClientIncidentRequest request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idCliente))
        {
            return Unauthorized();
        }

        if (request.IdPaquete is null or <= 0)
        {
            return BadRequest(new { message = "El identificador del paquete no es válido." });
        }

        if (string.IsNullOrWhiteSpace(request.TipoIncidencia)
            || !AllowedTypes.Contains(request.TipoIncidencia.Trim()))
        {
            return BadRequest(new
            {
                message = "El tipo de incidencia debe ser no_llegado, direccion, danado u otro."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Comentario))
        {
            return BadRequest(new { message = "El comentario es obligatorio." });
        }

        var incident = await incidentDb.CreateClientIncidentAsync(
            idCliente, request, cancellationToken);
        if (incident is null)
        {
            return NotFound(new { message = "No se encontró el paquete para este cliente." });
        }

        return StatusCode(StatusCodes.Status201Created, new IncidentResponse(
            incident.IdIncidencia,
            incident.IdPaquete,
            incident.IdRepartidor,
            incident.TipoIncidencia,
            incident.Comentario,
            incident.UrlFotoReporte,
            incident.FechaHora,
            incident.Folio,
            incident.Repartidor));
    }
}
