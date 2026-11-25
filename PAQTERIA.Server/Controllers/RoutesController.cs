using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using PAQTERIA.Server.Data;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[Route("api/routes")]
[Authorize(Roles = "Admin")]
public sealed class RoutesController(RouteDb routeDb) : ControllerBase
{
    [HttpGet("options")]
    public async Task<ActionResult<RouteOptionsResponse>> GetOptions(CancellationToken cancellationToken)
        => Ok(await routeDb.GetOptionsAsync(cancellationToken));

    [HttpGet("monitoring")]
    public async Task<ActionResult<RouteMonitoringResponse>> GetMonitoring(CancellationToken cancellationToken)
        => Ok(await routeDb.GetMonitoringAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<RouteAssignmentResponse>> CreateRoute(
        [FromBody] RouteAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = RouteRequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        try
        {
            var idTurno = await routeDb.CreateRouteAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created,
                new RouteAssignmentResponse(idTurno, request.IdPaquetes!.Count, "La ruta se asignó y guardó correctamente."));
        }
        catch (SqlException exception) when (exception.Number is >= 51000 and <= 51003)
        {
            return BadRequest(new { message = exception.Message.Replace("\r", string.Empty).Replace("\n", string.Empty) });
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return BadRequest(new { message = "Uno de los identificadores relacionados no existe." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
