using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PAQTERIA.Server.Data;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[Route("api/entregas")]
[Authorize]
public sealed class EntregasController(EntregaDb entregaDb, IWebHostEnvironment environment) : ControllerBase
{
    private const long MaxFotoBytes = 10 * 1024 * 1024;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 12 * 1024 * 1024)]
    public async Task<IActionResult> Registrar(
        [FromForm] string? folio,
        [FromForm] string? nombreReceptor,
        [FromForm] string? firmaSvg,
        [FromForm] IFormFile? foto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folio)) return BadRequest(new { message = "El folio es obligatorio." });
        if (string.IsNullOrWhiteSpace(nombreReceptor)) return BadRequest(new { message = "El nombre del receptor es obligatorio." });
        if (nombreReceptor.Length > 150) return BadRequest(new { message = "El nombre del receptor no puede superar 150 caracteres." });
        if (string.IsNullOrWhiteSpace(firmaSvg)) return BadRequest(new { message = "La firma SVG es obligatoria." });
        if (foto is null || foto.Length == 0) return BadRequest(new { message = "La foto es obligatoria." });
        if (foto.Length > MaxFotoBytes) return BadRequest(new { message = "La foto no puede superar 10 MB." });

        var extension = foto.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/heic" => ".heic",
            _ => null
        };
        if (extension is null)
            return BadRequest(new { message = "La foto debe ser JPEG, PNG o HEIC." });

        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var idRepartidor))
            return Unauthorized(new { message = "No se pudo identificar al usuario de la sesión." });

        var normalizedFolio = folio.Trim();
        var safeFolio = string.Concat(normalizedFolio.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{safeFolio}_{stamp}{extension}";
        var signatureName = $"{safeFolio}_{stamp}_firma.svg";
        var directory = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads", "entregas");
        Directory.CreateDirectory(directory);
        var photoPath = Path.Combine(directory, fileName);
        var signaturePath = Path.Combine(directory, signatureName);
        var urlPhoto = $"/uploads/entregas/{fileName}";
        var urlSignature = $"/uploads/entregas/{signatureName}";

        try
        {
            await using (var stream = System.IO.File.Create(photoPath))
                await foto.CopyToAsync(stream, cancellationToken);
            await System.IO.File.WriteAllTextAsync(signaturePath, firmaSvg, cancellationToken);

            var (result, entrega) = await entregaDb.RegistrarAsync(
                normalizedFolio, idRepartidor, nombreReceptor.Trim(), urlPhoto, urlSignature, cancellationToken);
            if (result != ResultadoEntrega.Creada)
            {
                DeleteIfExists(photoPath);
                DeleteIfExists(signaturePath);
                return result == ResultadoEntrega.PaqueteNoEncontrado
                    ? NotFound(new { message = "No existe un paquete con ese folio." })
                    : Conflict(new { message = "El paquete ya fue entregado." });
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                idComprobante = entrega!.IdComprobante,
                urlFoto = urlPhoto,
                urlFirma = urlSignature
            });
        }
        catch
        {
            DeleteIfExists(photoPath);
            DeleteIfExists(signaturePath);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "No se pudo registrar la entrega." });
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
    }
}
