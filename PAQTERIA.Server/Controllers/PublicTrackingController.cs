using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PAQTERIA.Server.Data;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/rastreo")]
public sealed class PublicTrackingController(PackageDb packageDb) : ControllerBase
{
    private const string NotFoundMessage = "No se pudo localizar el envío con esos datos.";

    [HttpGet("{folio}")]
    [EnableRateLimiting("public-tracking")]
    public async Task<ActionResult<PublicTrackingResponse>> GetTracking(
        string folio,
        [FromQuery] string? verificacion,
        CancellationToken cancellationToken)
    {
        var tracking = await packageDb.GetPublicTrackingAsync(
            folio.Trim(), verificacion, cancellationToken);
        return tracking is null
            ? NotFound(new { message = NotFoundMessage })
            : Ok(tracking);
    }
}
