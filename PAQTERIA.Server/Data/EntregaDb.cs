using Microsoft.Data.SqlClient;

namespace PAQTERIA.Server.Data;

public enum ResultadoEntrega
{
    Creada,
    PaqueteNoEncontrado,
    YaEntregado
}

public sealed record EntregaCreada(int IdComprobante);

public sealed class EntregaDb(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se configuró ConnectionStrings:DefaultConnection.");

    public async Task<(ResultadoEntrega Resultado, EntregaCreada? Entrega)> RegistrarAsync(
        string folio, int idRepartidor, string nombreReceptor, string urlFoto, string urlFirma,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            int idPaquete;
            string estadoActual;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT id_paquete, estado_actual
                    FROM dbo.PAQUETES WITH (UPDLOCK, HOLDLOCK)
                    WHERE folio = @folio;
                    """;
                command.Parameters.AddWithValue("@folio", folio);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return (ResultadoEntrega.PaqueteNoEncontrado, null);
                }
                idPaquete = reader.GetInt32(0);
                estadoActual = reader.GetString(1);
            }

            if (string.Equals(estadoActual, "Entregado", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.RollbackAsync(cancellationToken);
                return (ResultadoEntrega.YaEntregado, null);
            }

            int idComprobante;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO dbo.COMPROBANTES_ENTREGA
                        (id_paquete, id_repartidor, nombre_receptor, url_foto_evidencia, url_firma_receptor)
                    OUTPUT INSERTED.id_comprobante
                    VALUES (@idPaquete, @idRepartidor, @nombreReceptor, @urlFoto, @urlFirma);
                    """;
                command.Parameters.AddWithValue("@idPaquete", idPaquete);
                command.Parameters.AddWithValue("@idRepartidor", idRepartidor);
                command.Parameters.AddWithValue("@nombreReceptor", nombreReceptor);
                command.Parameters.AddWithValue("@urlFoto", urlFoto);
                command.Parameters.AddWithValue("@urlFirma", urlFirma);
                idComprobante = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE dbo.PAQUETES SET estado_actual = N'Entregado' WHERE id_paquete = @idPaquete;
                    UPDATE dbo.PARADAS_RUTA SET estado_parada = N'Entregado' WHERE id_paquete = @idPaquete;
                    INSERT INTO dbo.HISTORIAL_SEGUIMIENTO (id_paquete, titulo, descripcion)
                    VALUES (@idPaquete, N'Entregado', @descripcion);
                    """;
                command.Parameters.AddWithValue("@idPaquete", idPaquete);
                command.Parameters.AddWithValue("@descripcion", $"Recibió: {nombreReceptor}");
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return (ResultadoEntrega.Creada, new EntregaCreada(idComprobante));
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
