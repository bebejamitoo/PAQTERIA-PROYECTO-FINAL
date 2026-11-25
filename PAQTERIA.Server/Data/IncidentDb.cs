using Microsoft.Data.SqlClient;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Data;

public sealed class IncidentDb(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se configuró ConnectionStrings:DefaultConnection.");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF COL_LENGTH(N'dbo.INCIDENCIAS_ENTREGA', N'id_repartidor') IS NOT NULL
               AND EXISTS
               (
                   SELECT 1
                   FROM sys.columns
                   WHERE object_id = OBJECT_ID(N'dbo.INCIDENCIAS_ENTREGA')
                     AND name = N'id_repartidor'
                     AND is_nullable = 0
               )
                ALTER TABLE dbo.INCIDENCIAS_ENTREGA ALTER COLUMN id_repartidor INT NULL;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IncidentOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var packages = new List<IncidentPackageOption>();
        var drivers = new List<IncidentDriverOption>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_paquete, folio, direccion_destino
                FROM dbo.PAQUETES
                ORDER BY fecha_creacion DESC, id_paquete DESC;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                packages.Add(new IncidentPackageOption(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_usuario, nombre
                FROM dbo.USUARIOS
                ORDER BY nombre;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                drivers.Add(new IncidentDriverOption(reader.GetInt32(0), reader.GetString(1)));
            }
        }

        return new IncidentOptionsResponse(packages, drivers);
    }

    public async Task<IReadOnlyList<DeliveryIncident>> GetIncidentsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ie.id_incidencia, ie.id_paquete, ie.id_repartidor,
                   ie.tipo_incidencia, ie.comentario, ie.url_foto_reporte,
                   ie.fecha_hora, p.folio, u.nombre
            FROM dbo.INCIDENCIAS_ENTREGA ie
            INNER JOIN dbo.PAQUETES p ON p.id_paquete = ie.id_paquete
            LEFT JOIN dbo.USUARIOS u ON u.id_usuario = ie.id_repartidor
            ORDER BY ie.fecha_hora DESC, ie.id_incidencia DESC;
            """;

        var incidents = new List<DeliveryIncident>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            incidents.Add(ReadIncident(reader));
        }

        return incidents;
    }

    public async Task<DeliveryIncident> CreateIncidentAsync(
        CreateIncidentRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.INCIDENCIAS_ENTREGA
                (id_paquete, id_repartidor, tipo_incidencia, comentario, url_foto_reporte, fecha_hora)
            OUTPUT INSERTED.id_incidencia
            VALUES
                (@idPaquete, @idRepartidor, @tipoIncidencia, @comentario, @urlFotoReporte, SYSDATETIME());
            """;
        command.Parameters.AddWithValue("@idPaquete", request.IdPaquete!.Value);
        command.Parameters.AddWithValue("@idRepartidor", request.IdRepartidor!.Value);
        command.Parameters.AddWithValue("@tipoIncidencia", request.TipoIncidencia!.Trim());
        command.Parameters.AddWithValue("@comentario", (object?)request.Comentario?.Trim() ?? DBNull.Value);
        command.Parameters.AddWithValue("@urlFotoReporte", (object?)request.UrlFotoReporte?.Trim() ?? DBNull.Value);

        var idIncidencia = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetIncidentByIdAsync(idIncidencia, connection, cancellationToken)
            ?? throw new InvalidOperationException("La incidencia se insertó, pero no pudo recuperarse.");
    }

    public async Task<DeliveryIncident?> CreateClientIncidentAsync(
        int idCliente,
        CreateClientIncidentRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.INCIDENCIAS_ENTREGA
                (id_paquete, id_repartidor, tipo_incidencia, comentario, url_foto_reporte, fecha_hora)
            OUTPUT INSERTED.id_incidencia
            SELECT p.id_paquete, ruta.id_repartidor, @tipoIncidencia, @comentario, NULL, SYSDATETIME()
            FROM dbo.PAQUETES p
            OUTER APPLY
            (
                SELECT TOP (1) tr.id_repartidor
                FROM dbo.PARADAS_RUTA pr
                INNER JOIN dbo.TURNOS_REPARTIDOR tr ON tr.id_turno = pr.id_turno
                WHERE pr.id_paquete = p.id_paquete
                  AND tr.estado_turno NOT IN (N'Cancelado', N'Finalizado')
                ORDER BY CASE WHEN tr.estado_turno = N'En curso' THEN 0 ELSE 1 END,
                         tr.fecha_turno DESC, tr.id_turno DESC
            ) ruta
            WHERE p.id_paquete = @idPaquete
              AND p.id_cliente = @idCliente;
            """;
        command.Parameters.AddWithValue("@idCliente", idCliente);
        command.Parameters.AddWithValue("@idPaquete", request.IdPaquete!.Value);
        command.Parameters.AddWithValue("@tipoIncidencia", request.TipoIncidencia!.Trim());
        command.Parameters.AddWithValue("@comentario", request.Comentario!.Trim());

        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull) return null;

        var idIncidencia = Convert.ToInt32(result);
        return await GetIncidentByIdAsync(idIncidencia, connection, cancellationToken);
    }

    private static async Task<DeliveryIncident?> GetIncidentByIdAsync(
        int idIncidencia,
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ie.id_incidencia, ie.id_paquete, ie.id_repartidor,
                   ie.tipo_incidencia, ie.comentario, ie.url_foto_reporte,
                   ie.fecha_hora, p.folio, u.nombre
            FROM dbo.INCIDENCIAS_ENTREGA ie
            INNER JOIN dbo.PAQUETES p ON p.id_paquete = ie.id_paquete
            LEFT JOIN dbo.USUARIOS u ON u.id_usuario = ie.id_repartidor
            WHERE ie.id_incidencia = @idIncidencia;
            """;
        command.Parameters.AddWithValue("@idIncidencia", idIncidencia);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadIncident(reader) : null;
    }

    private static DeliveryIncident ReadIncident(SqlDataReader reader) => new()
    {
        IdIncidencia = reader.GetInt32(0),
        IdPaquete = reader.GetInt32(1),
        IdRepartidor = reader.IsDBNull(2) ? null : reader.GetInt32(2),
        TipoIncidencia = reader.GetString(3),
        Comentario = reader.IsDBNull(4) ? null : reader.GetString(4),
        UrlFotoReporte = reader.IsDBNull(5) ? null : reader.GetString(5),
        FechaHora = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
        Folio = reader.GetString(7),
        Repartidor = reader.IsDBNull(8) ? null : reader.GetString(8),
    };

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
