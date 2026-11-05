using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PAQTERIA.Server.Data;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthDb authDb) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthUserResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "El correo y la contraseña son obligatorios." });
        }

        var user = await authDb.ValidateUserAsync(
            request.Email.Trim(), request.Password, cancellationToken);
        if (user is null)
        {
            return Unauthorized(new { message = "Las credenciales no son válidas." });
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.IdUsuario.ToString()),
            new Claim(ClaimTypes.Name, user.Nombre),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Rol)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Ok(user);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("me")]
    public ActionResult<AuthUserResponse> Me()
        => Ok(new AuthUserResponse(
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            User.Identity?.Name ?? string.Empty,
            User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            User.FindFirstValue(ClaimTypes.Role) ?? string.Empty));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<UserManagementResponse>>> GetUsers(
        CancellationToken cancellationToken)
    {
        var users = await authDb.GetUsersAsync(cancellationToken);
        return Ok(users);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("users/options")]
    public async Task<ActionResult<UserOptionsResponse>> GetUserOptions(
        CancellationToken cancellationToken)
    {
        return Ok(await authDb.GetUserOptionsAsync(cancellationToken));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("users")]
    public async Task<ActionResult<UserManagementResponse>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateUser(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        try
        {
            var user = await authDb.CreateUserAsync(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, user);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Conflict(new { message = "Ya existe un usuario con ese correo electrónico." });
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 547)
        {
            return BadRequest(new { message = "El rol seleccionado no existe." });
        }
    }

    private static string? ValidateUser(CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Nombre)) return "El nombre completo es obligatorio.";
        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@')) return "El correo electrónico no es válido.";
        if (request.IdRol is null or <= 0) return "Selecciona un rol.";
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 12) return "La contraseña debe tener mínimo 12 caracteres.";
        return null;
    }
}
