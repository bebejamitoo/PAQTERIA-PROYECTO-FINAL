using Microsoft.Data.SqlClient;
using System.Globalization;
using PAQTERIA.Server.Models;

namespace PAQTERIA.Server.Data;

public sealed class RouteDb(IConfiguration configuration, OsrmClient osrmClient)
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("No se configuró ConnectionStrings:DefaultConnection.");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF COL_LENGTH('dbo.TURNOS_REPARTIDOR', 'geometria_geojson') IS NULL
                ALTER TABLE dbo.TURNOS_REPARTIDOR ADD geometria_geojson NVARCHAR(MAX) NULL;
            IF COL_LENGTH('dbo.TURNOS_REPARTIDOR', 'distancia_metros') IS NULL
                ALTER TABLE dbo.TURNOS_REPARTIDOR ADD distancia_metros FLOAT NULL;
            IF COL_LENGTH('dbo.TURNOS_REPARTIDOR', 'duracion_segundos') IS NULL
                ALTER TABLE dbo.TURNOS_REPARTIDOR ADD duracion_segundos FLOAT NULL;
            IF COL_LENGTH('dbo.TURNOS_REPARTIDOR', 'origen_latitud') IS NULL
                ALTER TABLE dbo.TURNOS_REPARTIDOR ADD origen_latitud FLOAT NULL;
            IF COL_LENGTH('dbo.TURNOS_REPARTIDOR', 'origen_longitud') IS NULL
                ALTER TABLE dbo.TURNOS_REPARTIDOR ADD origen_longitud FLOAT NULL;
            IF OBJECT_ID('dbo.POSICIONES_REPARTIDOR', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.POSICIONES_REPARTIDOR
                (
                    id_posicion INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    id_turno INT NOT NULL,
                    id_repartidor INT NOT NULL,
                    latitud FLOAT NOT NULL,
                    longitud FLOAT NOT NULL,
                    precision_metros FLOAT NULL,
                    velocidad_kmh FLOAT NULL,
                    rumbo_grados FLOAT NULL,
                    fecha_hora_utc DATETIME2(3) NOT NULL,
                    fecha_recepcion_utc DATETIME2(3) NOT NULL CONSTRAINT DF_POSICIONES_REPARTIDOR_RECEPCION DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT FK_POSICIONES_REPARTIDOR_TURNO
                        FOREIGN KEY (id_turno) REFERENCES dbo.TURNOS_REPARTIDOR(id_turno),
                    CONSTRAINT FK_POSICIONES_REPARTIDOR_USUARIO
                        FOREIGN KEY (id_repartidor) REFERENCES dbo.USUARIOS(id_usuario)
                );
                CREATE INDEX IX_POSICIONES_REPARTIDOR_TURNO_FECHA
                    ON dbo.POSICIONES_REPARTIDOR(id_turno, fecha_hora_utc DESC);
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RouteOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var drivers = new List<RouteDriverOption>();
        var units = new List<RouteUnitOption>();
        var packages = new List<RoutePackageOption>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_usuario, nombre, email
                FROM dbo.USUARIOS
                ORDER BY nombre;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                drivers.Add(new RouteDriverOption(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id_unidad, codigo_unidad, placas
                FROM dbo.UNIDADES
                ORDER BY codigo_unidad;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                units.Add(new RouteUnitOption(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT p.id_paquete, p.folio, p.direccion_destino,
                   ISNULL(p.es_prioritario, 0), p.peso_kg, p.coordenadas_destino
                FROM dbo.PAQUETES p
                WHERE NOT EXISTS
                (
                    SELECT 1
                    FROM dbo.PARADAS_RUTA pr
                    INNER JOIN dbo.TURNOS_REPARTIDOR tr ON tr.id_turno = pr.id_turno
                    WHERE pr.id_paquete = p.id_paquete
                      AND tr.estado_turno NOT IN (N'Cancelado', N'Finalizado')
                )
                ORDER BY ISNULL(p.es_prioritario, 0) DESC, p.fecha_creacion, p.id_paquete;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                packages.Add(new RoutePackageOption(
                    reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                    reader.GetBoolean(3), reader.GetDecimal(4), ParseCoordinate(reader.GetString(5)).Latitude,
                    ParseCoordinate(reader.GetString(5)).Longitude));
            }
        }

        return new RouteOptionsResponse(drivers, units, packages);
    }

    public async Task<RouteMonitoringResponse> GetMonitoringAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var turns = new List<RouteMonitoringItem>();
        var totalPackages = 0;
        var pendingStops = 0;
        var openIncidents = 0;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT tr.id_turno, tr.fecha_turno,
                       COALESCE(u.nombre, N'Sin repartidor') AS repartidor,
                       COALESCE(un.codigo_unidad + N' - ' + un.placas, N'Sin unidad') AS unidad,
                       tr.estado_turno,
                       COALESCE(p.total_paquetes, 0) AS total_paquetes,
                       COALESCE(p.pendientes, 0) AS pendientes,
                       COALESCE(i.incidencias, 0) AS incidencias,
                       tr.geometria_geojson, tr.distancia_metros, tr.duracion_segundos
                FROM dbo.TURNOS_REPARTIDOR tr
                LEFT JOIN dbo.USUARIOS u ON u.id_usuario = tr.id_repartidor
                LEFT JOIN dbo.UNIDADES un ON un.id_unidad = tr.id_unidad
                LEFT JOIN (
                    SELECT id_turno,
                           COUNT(id_parada) AS total_paquetes,
                           SUM(CASE
                                   WHEN estado_parada NOT IN (N'Entregada', N'Cancelada')
                                   THEN 1
                                   ELSE 0
                               END) AS pendientes
                    FROM dbo.PARADAS_RUTA
                    GROUP BY id_turno
                ) p ON p.id_turno = tr.id_turno
                LEFT JOIN (
                    SELECT pr.id_turno,
                           COUNT(DISTINCT ie.id_incidencia) AS incidencias
                    FROM dbo.PARADAS_RUTA pr
                    LEFT JOIN dbo.INCIDENCIAS_ENTREGA ie ON ie.id_paquete = pr.id_paquete
                    GROUP BY pr.id_turno
                ) i ON i.id_turno = tr.id_turno
                ORDER BY tr.fecha_turno DESC, tr.id_turno DESC;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var item = new RouteMonitoringItem(
                    reader.GetInt32(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetInt32(5),
                        reader.GetInt32(6), reader.GetInt32(7), Array.Empty<RouteStopItem>(),
                        reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetDouble(9),
                        reader.IsDBNull(10) ? null : reader.GetDouble(10));

                turns.Add(item);
                totalPackages += item.TotalPaquetes;
                pendingStops += item.ParadasPendientes;
                openIncidents += item.Incidencias;
            }
        }

        for (var i = 0; i < turns.Count; i++)
        {
            var turn = turns[i];
            var stops = await GetStopsAsync(connection, turn.IdTurno, cancellationToken);
            turns[i] = turn with { Paradas = stops };
        }

        return new RouteMonitoringResponse(
            turns,
            totalPackages,
            pendingStops,
            openIncidents);
    }

    public async Task<MobileActiveRouteResponse?> GetActiveRouteAsync(
        int idRepartidor,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1)
                   tr.id_turno, tr.fecha_turno, tr.estado_turno, tr.total_paquetes,
                   tr.geometria_geojson, tr.distancia_metros, tr.duracion_segundos,
                   pos.latitud, pos.longitud, pos.precision_metros,
                   pos.velocidad_kmh, pos.rumbo_grados, pos.fecha_hora_utc
            FROM dbo.TURNOS_REPARTIDOR tr
            OUTER APPLY
            (
                SELECT TOP (1) p.latitud, p.longitud, p.precision_metros,
                       p.velocidad_kmh, p.rumbo_grados, p.fecha_hora_utc
                FROM dbo.POSICIONES_REPARTIDOR p
                WHERE p.id_turno = tr.id_turno
                ORDER BY p.fecha_hora_utc DESC, p.id_posicion DESC
            ) pos
            WHERE tr.id_repartidor = @idRepartidor
              AND tr.fecha_turno >= CONVERT(date, SYSDATETIME())
              AND tr.fecha_turno < DATEADD(day, 1, CONVERT(date, SYSDATETIME()))
              AND tr.estado_turno NOT IN (N'Cancelado', N'Finalizado')
            ORDER BY CASE WHEN tr.estado_turno = N'En curso' THEN 0 ELSE 1 END,
                     tr.id_turno DESC;
            """;
        command.Parameters.AddWithValue("@idRepartidor", idRepartidor);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        MobileLastLocationResponse? lastLocation = reader.IsDBNull(7)
            ? null
            : new MobileLastLocationResponse(
                reader.GetDouble(7), reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.GetDateTime(12));

        return new MobileActiveRouteResponse(
            reader.GetInt32(0), reader.GetFieldValue<DateOnly>(1), reader.GetString(2),
            reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDouble(5),
            reader.IsDBNull(6) ? null : reader.GetDouble(6), lastLocation);
    }

    public async Task<bool> StartRouteAsync(
        int idTurno,
        int idRepartidor,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.TURNOS_REPARTIDOR
            SET estado_turno = N'En curso'
            WHERE id_turno = @idTurno
              AND id_repartidor = @idRepartidor
              AND fecha_turno >= CONVERT(date, SYSDATETIME())
              AND fecha_turno < DATEADD(day, 1, CONVERT(date, SYSDATETIME()))
              AND estado_turno = N'Asignado';
            """;
        command.Parameters.AddWithValue("@idTurno", idTurno);
        command.Parameters.AddWithValue("@idRepartidor", idRepartidor);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> SaveLocationAsync(
        int idTurno,
        int idRepartidor,
        MobileLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO dbo.POSICIONES_REPARTIDOR
                (id_turno, id_repartidor, latitud, longitud, precision_metros,
                 velocidad_kmh, rumbo_grados, fecha_hora_utc)
            SELECT @idTurno, @idRepartidor, @latitud, @longitud, @precisionMetros,
                   @velocidadKmh, @rumboGrados, @fechaHoraUtc
            WHERE EXISTS
            (
                SELECT 1
                FROM dbo.TURNOS_REPARTIDOR
                WHERE id_turno = @idTurno
                  AND id_repartidor = @idRepartidor
                  AND estado_turno = N'En curso'
            );
            """;
        command.Parameters.AddWithValue("@idTurno", idTurno);
        command.Parameters.AddWithValue("@idRepartidor", idRepartidor);
        command.Parameters.AddWithValue("@latitud", request.Latitud!.Value);
        command.Parameters.AddWithValue("@longitud", request.Longitud!.Value);
        command.Parameters.AddWithValue("@precisionMetros", (object?)request.PrecisionMetros ?? DBNull.Value);
        command.Parameters.AddWithValue("@velocidadKmh", (object?)request.VelocidadKmh ?? DBNull.Value);
        command.Parameters.AddWithValue("@rumboGrados", (object?)request.RumboGrados ?? DBNull.Value);
        command.Parameters.AddWithValue("@fechaHoraUtc", request.FechaHoraUtc!.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<int> CreateRouteAsync(RouteAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var route = await CalculateRouteAsync(connection, request, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await EnsureReferencesAsync(connection, transaction, request, cancellationToken);
            await using var turnCommand = new SqlCommand("""
                INSERT INTO dbo.TURNOS_REPARTIDOR
                    (id_repartidor, id_unidad, fecha_turno, estado_turno, total_paquetes, tiempo_estimado_horas,
                     geometria_geojson, distancia_metros, duracion_segundos, origen_latitud, origen_longitud)
                OUTPUT INSERTED.id_turno
                VALUES (@idRepartidor, @idUnidad, @fechaTurno, N'Asignado', @totalPaquetes, @tiempoEstimadoHoras,
                        @geometria, @distancia, @duracion, @origenLatitud, @origenLongitud);
                """, connection, transaction);
            turnCommand.Parameters.AddWithValue("@idRepartidor", request.IdRepartidor!.Value);
            turnCommand.Parameters.AddWithValue("@idUnidad", request.IdUnidad!.Value);
            turnCommand.Parameters.AddWithValue("@fechaTurno", request.FechaTurno!.Value.ToDateTime(TimeOnly.MinValue));
            turnCommand.Parameters.AddWithValue("@totalPaquetes", request.IdPaquetes!.Count);
            turnCommand.Parameters.AddWithValue("@tiempoEstimadoHoras", request.TiempoEstimadoHoras!.Value);
            turnCommand.Parameters.AddWithValue("@geometria", route.Geometry);
            turnCommand.Parameters.AddWithValue("@distancia", route.DistanceMeters);
            turnCommand.Parameters.AddWithValue("@duracion", route.DurationSeconds);
            turnCommand.Parameters.AddWithValue("@origenLatitud", request.OrigenLatitud!.Value);
            turnCommand.Parameters.AddWithValue("@origenLongitud", request.OrigenLongitud!.Value);
            var idTurno = Convert.ToInt32(await turnCommand.ExecuteScalarAsync(cancellationToken));

            for (var index = 0; index < request.IdPaquetes.Count; index++)
            {
                var idPaquete = request.IdPaquetes[index];
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO dbo.PARADAS_RUTA (id_turno, id_paquete, orden_secuencia, estado_parada)
                    VALUES (@idTurno, @idPaquete, @orden, N'Pendiente');
                    UPDATE dbo.PAQUETES SET estado_actual = N'En ruta' WHERE id_paquete = @idPaquete;
                    INSERT INTO dbo.HISTORIAL_SEGUIMIENTO (id_paquete, titulo, descripcion, fecha_hora)
                    VALUES (@idPaquete, N'Ruta asignada', @descripcion, SYSUTCDATETIME());
                    """, new Dictionary<string, object?>
                {
                    ["@idTurno"] = idTurno,
                    ["@idPaquete"] = idPaquete,
                    ["@orden"] = index + 1,
                    ["@descripcion"] = $"Asignado al turno {idTurno}, parada {index + 1}."
                }, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return idTurno;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task EnsureReferencesAsync(SqlConnection connection, SqlTransaction transaction, RouteAssignmentRequest request, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.USUARIOS WHERE id_usuario = @idRepartidor)
                THROW 51000, 'El repartidor no existe.', 1;
            IF NOT EXISTS (SELECT 1 FROM dbo.UNIDADES WHERE id_unidad = @idUnidad)
                THROW 51001, 'La unidad no existe.', 1;
            IF (SELECT COUNT(*) FROM dbo.PAQUETES p WHERE p.id_paquete IN (SELECT value FROM OPENJSON(@ids))) <> @total
                THROW 51002, 'Uno o más paquetes no existen.', 1;
            IF EXISTS
            (
                SELECT 1 FROM dbo.PARADAS_RUTA pr
                INNER JOIN dbo.TURNOS_REPARTIDOR tr ON tr.id_turno = pr.id_turno
                WHERE pr.id_paquete IN (SELECT value FROM OPENJSON(@ids))
                  AND tr.estado_turno NOT IN (N'Cancelado', N'Finalizado')
            )
                THROW 51003, 'Uno o más paquetes ya están asignados a una ruta.', 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@idRepartidor", request.IdRepartidor!.Value);
        command.Parameters.AddWithValue("@idUnidad", request.IdUnidad!.Value);
        command.Parameters.AddWithValue("@ids", $"[{string.Join(',', request.IdPaquetes!)}]");
        command.Parameters.AddWithValue("@total", request.IdPaquetes.Count);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<RouteStopItem>> GetStopsAsync(SqlConnection connection, int idTurno, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pr.id_parada, pr.id_paquete, p.folio, pr.orden_secuencia,
                   pr.estado_parada, p.direccion_destino, p.estado_actual,
                   COALESCE((SELECT TOP 1 hs.titulo FROM dbo.HISTORIAL_SEGUIMIENTO hs
                             WHERE hs.id_paquete = p.id_paquete ORDER BY hs.fecha_hora DESC, hs.id_historial DESC), N'Sin eventos')
            FROM dbo.PARADAS_RUTA pr
            INNER JOIN dbo.PAQUETES p ON p.id_paquete = pr.id_paquete
            WHERE pr.id_turno = @idTurno
            ORDER BY pr.orden_secuencia;
            """;
        command.Parameters.AddWithValue("@idTurno", idTurno);
        var stops = new List<RouteStopItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            stops.Add(new RouteStopItem(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        }
        return stops;
    }

    private async Task<RouteCalculation> CalculateRouteAsync(
        SqlConnection connection,
        RouteAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id_paquete, coordenadas_destino
            FROM dbo.PAQUETES
            WHERE id_paquete IN (SELECT value FROM OPENJSON(@ids));
            """;
        command.Parameters.AddWithValue("@ids", $"[{string.Join(',', request.IdPaquetes!)}]");
        var coordinatesByPackage = new Dictionary<int, RouteCoordinate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            coordinatesByPackage[reader.GetInt32(0)] = ParseCoordinate(reader.GetString(1));
        }

        var points = new List<RouteCoordinate> { new(request.OrigenLatitud!.Value, request.OrigenLongitud!.Value) };
        foreach (var idPaquete in request.IdPaquetes)
        {
            if (!coordinatesByPackage.TryGetValue(idPaquete, out var coordinate))
                throw new InvalidOperationException("Uno de los paquetes no tiene coordenadas de destino válidas.");
            points.Add(coordinate);
        }

        return await osrmClient.CalculateAsync(points, cancellationToken);
    }

    private static RouteCoordinate ParseCoordinate(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
            || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new InvalidOperationException("Una coordenada de destino no tiene el formato latitud, longitud.");
        }
        return new RouteCoordinate(latitude, longitude);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, Dictionary<string, object?> values, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        foreach (var value in values) command.Parameters.AddWithValue(value.Key, value.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
