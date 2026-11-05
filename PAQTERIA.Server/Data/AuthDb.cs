using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Data;

public sealed class AuthDb(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se configuró ConnectionStrings:DefaultConnection.");

    public async Task<AuthUserResponse?> ValidateUserAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) u.id_usuario, u.nombre, u.email, u.password_hash,
                   u.id_rol, r.nombre
            FROM dbo.USUARIOS u
            INNER JOIN dbo.ROLES r ON r.id_rol = u.id_rol
            WHERE u.email = @email;
            """;
        command.Parameters.AddWithValue("@email", email);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var storedHash = reader.GetString(3);
        if (!VerifyPassword(storedHash, password)) return null;

        var idRol = reader.GetInt32(4);
        var rol = idRol == 1 ? "Admin" : reader.GetString(5);
        return new AuthUserResponse(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), rol);
    }

    public async Task<UserOptionsResponse> GetUserOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_rol, nombre
            FROM dbo.ROLES
            ORDER BY id_rol;
            """;

        var roles = new List<RoleOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new RoleOption(reader.GetInt32(0), reader.GetString(1)));
        }

        return new UserOptionsResponse(roles);
    }

    public async Task<IReadOnlyList<UserManagementResponse>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id_usuario, u.id_rol, u.nombre, u.email, u.telefono,
                   r.nombre, u.fecha_registro
            FROM dbo.USUARIOS u
            INNER JOIN dbo.ROLES r ON r.id_rol = u.id_rol
            ORDER BY u.id_usuario DESC;
            """;

        var users = new List<UserManagementResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(ReadUser(reader));
        }

        return users;
    }

    public async Task<UserManagementResponse> CreateUserAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.USUARIOS
                (id_rol, nombre, email, telefono, password_hash, fecha_registro)
            OUTPUT INSERTED.id_usuario
            VALUES
                (@idRol, @nombre, @email, @telefono, @passwordHash, SYSDATETIME());
            """;
        command.Parameters.AddWithValue("@idRol", request.IdRol!.Value);
        command.Parameters.AddWithValue("@nombre", request.Nombre!.Trim());
        command.Parameters.AddWithValue("@email", request.Email!.Trim());
        command.Parameters.AddWithValue("@telefono", (object?)request.Telefono?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("@passwordHash", HashPassword(request.Password!));

        var idUsuario = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetUserByIdAsync(idUsuario, connection, cancellationToken)
            ?? throw new InvalidOperationException("El usuario se insertó, pero no pudo recuperarse.");
    }

    private static async Task<UserManagementResponse?> GetUserByIdAsync(
        int idUsuario,
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id_usuario, u.id_rol, u.nombre, u.email, u.telefono,
                   r.nombre, u.fecha_registro
            FROM dbo.USUARIOS u
            INNER JOIN dbo.ROLES r ON r.id_rol = u.id_rol
            WHERE u.id_usuario = @idUsuario;
            """;
        command.Parameters.AddWithValue("@idUsuario", idUsuario);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    private static UserManagementResponse ReadUser(SqlDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetInt32(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetDateTime(6));

    private static string HashPassword(string password)
        => new PasswordHasher<object>().HashPassword(null!, password);

    private static bool VerifyPassword(string storedHash, string password)
    {
        try
        {
            var hasher = new PasswordHasher<object>();
            var result = hasher.VerifyHashedPassword(null!, storedHash, password);
            if (result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded)
            {
                return true;
            }
        }
        catch (FormatException)
        {
            // El valor almacenado usa el formato heredado, no el de PasswordHasher.
        }

        // Compatibilidad temporal con los datos de demostración actuales.
        var expected = Encoding.UTF8.GetBytes(storedHash);
        var actual = Encoding.UTF8.GetBytes(password);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
