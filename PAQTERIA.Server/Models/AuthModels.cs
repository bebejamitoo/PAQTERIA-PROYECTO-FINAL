namespace PAQTERIA.Server.Models;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record AuthUserResponse(int IdUsuario, string Nombre, string Email, string Rol);

public sealed record UserManagementResponse(
    int IdUsuario,
    int IdRol,
    string Nombre,
    string Email,
    string? Telefono,
    string Rol,
    DateTime? FechaRegistro);

public sealed record RoleOption(int IdRol, string Nombre);

public sealed record UserOptionsResponse(IReadOnlyList<RoleOption> Roles);

public sealed record CreateUserRequest(
    string? Nombre,
    string? Email,
    string? Telefono,
    int? IdRol,
    string? Password);
