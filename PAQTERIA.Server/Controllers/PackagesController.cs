using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PAQTERIA.Server.Data;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[Route("api/packages")]
[Authorize(Roles = "Admin")]
public sealed class PackagesController(PackageDb packageDb) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PackageResponse>>> GetPackages(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var packages = await packageDb.GetPackagesAsync(search, cancellationToken);
        return Ok(packages.Select(ToResponse).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<PackageResponse>> CreatePackage(
        [FromBody] CreatePackageRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = PackageRequestValidation.Validate(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        if (!await packageDb.DistributionCenterExistsAsync(request.IdCentroOrigen!.Value, cancellationToken))
        {
            return BadRequest(new { message = "El centro de distribución indicado no existe." });
        }

        try
        {
            var package = await packageDb.CreatePackageAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, ToResponse(package));
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Conflict(new { message = "Ya existe un paquete con ese folio." });
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return BadRequest(new
            {
                message = "El identificador relacionado no existe. Comprueba el centro o la unidad de origen."
            });
        }
    }

    [HttpPost("{folio}/enter")]
    public async Task<ActionResult<PackageResponse>> EnterPackage(
        string folio,
        CancellationToken cancellationToken)
    {
        var normalizedFolio = folio.Trim();
        if (normalizedFolio.Length == 0)
        {
            return BadRequest(new { message = "El folio del paquete es obligatorio." });
        }

        var package = await packageDb.EnterPackageAsync(normalizedFolio, cancellationToken);
        return package is null
            ? NotFound(new { message = "No existe un paquete con ese folio." })
            : Ok(ToResponse(package));
    }

    private static PackageResponse ToResponse(Package package) => new(
        package.IdPaquete,
        package.Folio,
        package.IdCentroOrigen,
        package.DireccionOrigen,
        package.DireccionDestino,
        package.CoordenadasDestino,
        package.PesoKg,
        package.TamanoEtiqueta,
        package.EsPrioritario,
        package.EsFragil,
        package.EstadoActual,
        package.FechaCreacion);
}
