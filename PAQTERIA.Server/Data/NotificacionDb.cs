using Microsoft.Data.SqlClient;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Data;

public sealed class NotificacionDb(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se configuró ConnectionStrings:DefaultConnection.");

    public async Task<IReadOnlyList<NotificacionResponse>> GetForUserAsync(
        int idUsuario, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (50) n.id_notificacion, n.titulo, n.mensaje, n.tipo,
                   p.folio, n.fecha_hora, n.leida
            FROM dbo.NOTIFICACIONES n
            LEFT JOIN dbo.PAQUETES p ON p.id_paquete = n.id_paquete
            WHERE n.id_usuario = @idUsuario
            ORDER BY n.fecha_hora DESC, n.id_notificacion DESC;
            """;
        command.Parameters.AddWithValue("@idUsuario", idUsuario);

        var results = new List<NotificacionResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new NotificacionResponse(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
                reader.GetBoolean(6)));
        }
        return results;
    }

    public async Task<bool> MarkReadAsync(int idNotificacion, int idUsuario, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.NOTIFICACIONES SET leida = 1
            WHERE id_notificacion = @idNotificacion AND id_usuario = @idUsuario;
            """;
        command.Parameters.AddWithValue("@idNotificacion", idNotificacion);
        command.Parameters.AddWithValue("@idUsuario", idUsuario);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task MarkAllReadAsync(int idUsuario, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.NOTIFICACIONES SET leida = 1 WHERE id_usuario = @idUsuario;";
        command.Parameters.AddWithValue("@idUsuario", idUsuario);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CrearAsync(
        SqlConnection connection, SqlTransaction transaction, int idUsuario,
        string titulo, string mensaje, string tipo, int? idPaquete,
        CancellationToken cancellationToken = default)
    {
        await using var command = new SqlCommand("""
            INSERT INTO dbo.NOTIFICACIONES (id_usuario, titulo, mensaje, tipo, id_paquete)
            VALUES (@idUsuario, @titulo, @mensaje, @tipo, @idPaquete);
            """, connection, transaction);
        command.Parameters.AddWithValue("@idUsuario", idUsuario);
        command.Parameters.AddWithValue("@titulo", titulo);
        command.Parameters.AddWithValue("@mensaje", mensaje);
        command.Parameters.AddWithValue("@tipo", tipo);
        command.Parameters.AddWithValue("@idPaquete", (object?)idPaquete ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
