using Microsoft.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using PAQTERIA.Server;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Data;

public sealed class PackageDb(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se configuró ConnectionStrings:DefaultConnection.");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT OBJECT_ID(N'dbo.PAQUETES', N'U');";
        var tableId = await command.ExecuteScalarAsync(cancellationToken);
        if (tableId is null or DBNull)
        {
            throw new InvalidOperationException("No existe la tabla PAQTERIA.dbo.PAQUETES.");
        }
    }

    public async Task<IReadOnlyList<Package>> GetPackagesAsync(string? search, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_paquete, folio, id_centro_origen,
                   direccion_origen, direccion_destino, coordenadas_destino,
                   peso_kg, tamano_etiqueta, es_prioritario, es_fragil,
                   estado_actual, fecha_creacion
            FROM dbo.PAQUETES
            WHERE @search = N''
               OR folio LIKE @pattern
               OR direccion_destino LIKE @pattern
               OR estado_actual LIKE @pattern
            ORDER BY fecha_creacion DESC;
            """;
        AddSearchParameters(command, search);

        var packages = new List<Package>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            packages.Add(ReadPackage(reader));
        }

        return packages;
    }

    public async Task<IReadOnlyList<ClientPackageResponse>> GetClientPackagesAsync(
        int idCliente,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_paquete, folio, direccion_destino, coordenadas_destino,
                   ISNULL(es_prioritario, 0), ISNULL(es_fragil, 0), estado_actual
            FROM dbo.PAQUETES
            WHERE id_cliente = @idCliente
            ORDER BY fecha_creacion DESC, id_paquete DESC;
            """;
        command.Parameters.AddWithValue("@idCliente", idCliente);

        var packages = new List<ClientPackageResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            packages.Add(ReadClientPackage(reader));
        }

        return packages;
    }

    public async Task<ClientPackageResponse?> GetClientPackageByFolioAsync(
        int idCliente,
        string folio,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_paquete, folio, direccion_destino, coordenadas_destino,
                   ISNULL(es_prioritario, 0), ISNULL(es_fragil, 0), estado_actual
            FROM dbo.PAQUETES
            WHERE id_cliente = @idCliente
              AND folio = @folio;
            """;
        command.Parameters.AddWithValue("@idCliente", idCliente);
        command.Parameters.AddWithValue("@folio", folio);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadClientPackage(reader) : null;
    }

    public async Task<PublicTrackingResponse?> GetPublicTrackingAsync(
        string folio,
        string? verification,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.folio, p.estado_actual, p.coordenadas_destino,
                   p.direccion_destino, u.telefono
            FROM dbo.PAQUETES p
            LEFT JOIN dbo.USUARIOS u ON u.id_usuario = p.id_cliente
            WHERE p.folio = @folio;
            """;
        command.Parameters.AddWithValue("@folio", folio);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || string.IsNullOrWhiteSpace(verification))
        {
            return null;
        }

        var phoneDigits = reader.IsDBNull(4)
            ? string.Empty
            : new string(reader.GetString(4).Where(char.IsAsciiDigit).ToArray());
        if (phoneDigits.Length < 4)
        {
            return null;
        }

        var expected = phoneDigits[^4..];
        var supplied = verification.Trim();
        if (supplied.Length != 4
            || supplied.Any(character => character is < '0' or > '9')
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(supplied)))
        {
            return null;
        }

        return new PublicTrackingResponse(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            GetLocality(reader.GetString(3)));
    }

    public async Task<IReadOnlyList<MobilePackageResponse>> GetMobilePackagesAsync(
        int idUsuario,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var today = MexicoTime.Today;
        var todayStart = today.ToDateTime(TimeOnly.MinValue);
        var tomorrowStart = today.AddDays(1).ToDateTime(TimeOnly.MinValue);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id_paquete, p.folio, p.direccion_destino,
                   ISNULL(p.es_prioritario, 0), ISNULL(p.es_fragil, 0),
                   p.estado_actual, p.coordenadas_destino,
                   parada.orden_secuencia, parada.estado_parada
            FROM dbo.PAQUETES p
            OUTER APPLY
            (
                SELECT TOP (1) pr.orden_secuencia, pr.estado_parada
                FROM dbo.PARADAS_RUTA pr
                INNER JOIN dbo.TURNOS_REPARTIDOR tr ON tr.id_turno = pr.id_turno
                WHERE pr.id_paquete = p.id_paquete
                  AND
                  (
                      @isAdmin = 1
                      OR
                      (
                          tr.id_repartidor = @idUsuario
                          AND tr.fecha_turno >= @todayStart
                          AND tr.fecha_turno < @tomorrowStart
                          AND tr.estado_turno NOT IN (N'Cancelado', N'Finalizado')
                          AND p.estado_actual NOT IN (N'Entregado', N'Cancelado', N'Cancelada')
                          AND pr.estado_parada NOT IN (N'Entregado', N'Entregada', N'Cancelado', N'Cancelada')
                      )
                  )
                ORDER BY
                    CASE
                        WHEN tr.fecha_turno >= @todayStart
                         AND tr.fecha_turno < @tomorrowStart
                         AND tr.estado_turno NOT IN (N'Cancelado', N'Finalizado')
                        THEN 0 ELSE 1
                    END,
                    tr.fecha_turno DESC, tr.id_turno DESC, pr.orden_secuencia
            ) parada
            WHERE @isAdmin = 1
               OR EXISTS
               (
                   SELECT 1
                   FROM dbo.PARADAS_RUTA pr
                   INNER JOIN dbo.TURNOS_REPARTIDOR tr ON tr.id_turno = pr.id_turno
                   WHERE pr.id_paquete = p.id_paquete
                     AND tr.id_repartidor = @idUsuario
                     AND tr.fecha_turno >= @todayStart
                     AND tr.fecha_turno < @tomorrowStart
                     AND tr.estado_turno NOT IN (N'Cancelado', N'Finalizado')
                     AND p.estado_actual NOT IN (N'Entregado', N'Cancelado', N'Cancelada')
                     AND pr.estado_parada NOT IN (N'Entregado', N'Entregada', N'Cancelado', N'Cancelada')
               )
            ORDER BY CASE WHEN parada.orden_secuencia IS NULL THEN 1 ELSE 0 END,
                     parada.orden_secuencia, p.fecha_creacion DESC;
            """;
        command.Parameters.AddWithValue("@idUsuario", idUsuario);
        command.Parameters.AddWithValue("@isAdmin", isAdmin);
        command.Parameters.AddWithValue("@todayStart", todayStart);
        command.Parameters.AddWithValue("@tomorrowStart", tomorrowStart);

        var packages = new List<MobilePackageResponse>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            packages.Add(new MobilePackageResponse(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return packages;
    }

    public async Task<Package> CreatePackageAsync(CreatePackageRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.PAQUETES
            (
                folio, id_centro_origen, direccion_origen,
                direccion_destino, coordenadas_destino, peso_kg,
                tamano_etiqueta, es_prioritario, es_fragil,
                estado_actual, fecha_creacion
            )
            OUTPUT INSERTED.id_paquete
            VALUES
            (
                @folio, @idCentroOrigen, @direccionOrigen,
                @direccionDestino, @coordenadasDestino, @pesoKg,
                @tamanoEtiqueta, @esPrioritario, @esFragil,
                N'En clasificación', SYSUTCDATETIME()
            );
            """;
        AddCreateParameters(command, request);

        var idPaquete = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetPackageByIdAsync(idPaquete, connection, cancellationToken)
            ?? throw new InvalidOperationException("El paquete se insertó, pero no pudo recuperarse.");
    }

    public async Task<Package> CreateClientPackageAsync(
        CreatePackageRequest request,
        int idCliente,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.PAQUETES
            (
                folio, id_cliente, id_centro_origen, direccion_origen,
                direccion_destino, coordenadas_destino, peso_kg,
                tamano_etiqueta, es_prioritario, es_fragil,
                estado_actual, fecha_creacion
            )
            OUTPUT INSERTED.id_paquete
            VALUES
            (
                @folio, @idCliente, @idCentroOrigen, @direccionOrigen,
                @direccionDestino, @coordenadasDestino, @pesoKg,
                @tamanoEtiqueta, @esPrioritario, @esFragil,
                N'En clasificación', SYSUTCDATETIME()
            );
            """;
        command.Parameters.AddWithValue("@idCliente", idCliente);
        AddCreateParameters(command, request);

        var idPaquete = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetPackageByIdAsync(idPaquete, connection, cancellationToken)
            ?? throw new InvalidOperationException("El pedido se creó, pero no pudo recuperarse.");
    }

    public async Task<bool> DistributionCenterExistsAsync(
        int idCentro,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM dbo.CENTROS_DISTRIBUCION
            WHERE id_centro = @idCentro;
            """;
        command.Parameters.AddWithValue("@idCentro", idCentro);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value) > 0;
    }

    public async Task<Package?> EnterPackageAsync(string folio, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.PAQUETES
            SET estado_actual = N'En clasificación'
            WHERE folio = @folio;
            """;
        command.Parameters.AddWithValue("@folio", folio);
        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        return affectedRows == 0
            ? null
            : await GetPackageByFolioAsync(folio, connection, cancellationToken);
    }

    private async Task<Package?> GetPackageByIdAsync(int idPaquete, SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_paquete, folio, id_centro_origen,
                   direccion_origen, direccion_destino, coordenadas_destino,
                   peso_kg, tamano_etiqueta, es_prioritario, es_fragil,
                   estado_actual, fecha_creacion
            FROM dbo.PAQUETES
            WHERE id_paquete = @idPaquete;
            """;
        command.Parameters.AddWithValue("@idPaquete", idPaquete);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPackage(reader) : null;
    }

    private async Task<Package?> GetPackageByFolioAsync(string folio, SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_paquete, folio, id_centro_origen,
                   direccion_origen, direccion_destino, coordenadas_destino,
                   peso_kg, tamano_etiqueta, es_prioritario, es_fragil,
                   estado_actual, fecha_creacion
            FROM dbo.PAQUETES
            WHERE folio = @folio;
            """;
        command.Parameters.AddWithValue("@folio", folio);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadPackage(reader) : null;
    }

    private static void AddSearchParameters(SqlCommand command, string? search)
    {
        var value = search?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("@search", value);
        command.Parameters.AddWithValue("@pattern", $"%{value}%");
    }

    private static void AddCreateParameters(SqlCommand command, CreatePackageRequest request)
    {
        command.Parameters.AddWithValue("@folio", request.Folio!.Trim());
        command.Parameters.AddWithValue("@idCentroOrigen", request.IdCentroOrigen!.Value);
        command.Parameters.AddWithValue("@direccionOrigen", request.DireccionOrigen!.Trim());
        command.Parameters.AddWithValue("@direccionDestino", request.DireccionDestino!.Trim());
        command.Parameters.AddWithValue("@coordenadasDestino", request.CoordenadasDestino!.Trim());
        command.Parameters.AddWithValue("@pesoKg", request.PesoKg!.Value);
        command.Parameters.AddWithValue("@tamanoEtiqueta", request.TamanoEtiqueta!.Trim());
        command.Parameters.AddWithValue("@esPrioritario", request.EsPrioritario);
        command.Parameters.AddWithValue("@esFragil", request.EsFragil);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static Package ReadPackage(SqlDataReader reader) => new()
    {
        IdPaquete = reader.GetInt32(0),
        Folio = reader.GetString(1),
        IdCentroOrigen = reader.GetInt32(2),
        DireccionOrigen = reader.GetString(3),
        DireccionDestino = reader.GetString(4),
        CoordenadasDestino = reader.IsDBNull(5) ? null : reader.GetString(5),
        PesoKg = reader.GetDecimal(6),
        TamanoEtiqueta = reader.GetString(7),
        EsPrioritario = !reader.IsDBNull(8) && reader.GetBoolean(8),
        EsFragil = !reader.IsDBNull(9) && reader.GetBoolean(9),
        EstadoActual = reader.GetString(10),
        FechaCreacion = reader.GetDateTime(11),
    };

    private static ClientPackageResponse ReadClientPackage(SqlDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetBoolean(4),
        reader.GetBoolean(5),
        reader.GetString(6));

    private static string? GetLocality(string address)
    {
        var parts = address.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var neighborhood = parts.FirstOrDefault(part =>
            part.StartsWith("Col.", StringComparison.OrdinalIgnoreCase)
            || part.StartsWith("Colonia ", StringComparison.OrdinalIgnoreCase)
            || part.StartsWith("Fracc.", StringComparison.OrdinalIgnoreCase)
            || part.StartsWith("Fraccionamiento ", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(neighborhood))
        {
            return neighborhood.Length <= 80 ? neighborhood : null;
        }

        var postalCity = parts.FirstOrDefault(part =>
            part.Length > 5 && part.Take(5).All(char.IsAsciiDigit) && char.IsWhiteSpace(part[5]));
        if (postalCity is not null)
        {
            var city = postalCity[5..].Trim();
            return city.Length is > 0 and <= 80 ? city : null;
        }

        var locality = parts[1];
        return locality.Length is > 0 and <= 80 ? locality : null;
    }
}
