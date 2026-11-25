using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PAQTERIA.Server.Data;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[Route("api/notificaciones")]
[Authorize]
public sealed class NotificacionesController(NotificacionDb notificacionDb) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificacionResponse>>> Get(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var idUsuario)) return Unauthorized();
        return Ok(await notificacionDb.GetForUserAsync(idUsuario, cancellationToken));
    }

    [HttpPost("{id:int}/leida")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var idUsuario)) return Unauthorized();
        return await notificacionDb.MarkReadAsync(id, idUsuario, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpPost("leidas")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var idUsuario)) return Unauthorized();
        await notificacionDb.MarkAllReadAsync(idUsuario, cancellationToken);
        return NoContent();
    }

    private bool TryGetCurrentUserId(out int idUsuario)
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out idUsuario);
}
