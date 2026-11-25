using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;
using PAQTERIA.Server.Data;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[Route("api/client/paquetes")]
[Authorize(Roles = "Cliente")]
public sealed class ClientPackagesController(PackageDb packageDb) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ClientPackageResponse>> CreatePackage(
        [FromBody] CreatePackageRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetClientId(out var idCliente)) return Unauthorized();

        var validationError = PackageRequestValidation.Validate(request);
        if (validationError is not null) return BadRequest(new { message = validationError });
        if (!await packageDb.DistributionCenterExistsAsync(request.IdCentroOrigen!.Value, cancellationToken))
        {
            return BadRequest(new { message = "El centro de distribución indicado no existe." });
        }

        try
        {
            var package = await packageDb.CreateClientPackageAsync(request, idCliente, cancellationToken);
            var response = ToResponse(package);
            return CreatedAtAction(nameof(GetPackageByFolio), new { folio = package.Folio }, response);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Conflict(new { message = "Ya existe un pedido con ese folio." });
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return BadRequest(new { message = "El identificador relacionado no existe." });
        }
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClientPackageResponse>>> GetPackages(
        CancellationToken cancellationToken)
    {
        if (!TryGetClientId(out var idCliente)) return Unauthorized();
        return Ok(await packageDb.GetClientPackagesAsync(idCliente, cancellationToken));
    }

    [HttpGet("{folio}")]
    public async Task<ActionResult<ClientPackageResponse>> GetPackageByFolio(
        string folio,
        CancellationToken cancellationToken)
    {
        if (!TryGetClientId(out var idCliente)) return Unauthorized();

        var package = await packageDb.GetClientPackageByFolioAsync(
            idCliente, folio.Trim(), cancellationToken);
        return package is null ? NotFound() : Ok(package);
    }

    private bool TryGetClientId(out int idCliente)
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idCliente);

    private static ClientPackageResponse ToResponse(Package package)
        => new(package.IdPaquete, package.Folio, package.DireccionDestino,
            package.CoordenadasDestino, package.EsPrioritario, package.EsFragil,
            package.EstadoActual);
}
