using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PAQTERIA.Server.Data;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[Route("api/mobile/paquetes")]
[Authorize]
public sealed class MobilePackagesController(PackageDb packageDb) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MobilePackageResponse>>> GetPackages(
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idUsuario))
        {
            return Unauthorized();
        }

        var isAdmin = User.IsInRole("Admin");
        var packages = await packageDb.GetMobilePackagesAsync(idUsuario, isAdmin, cancellationToken);
        return Ok(packages);
    }
}
