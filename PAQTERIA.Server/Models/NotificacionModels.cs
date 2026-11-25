namespace PAQTERIA.Server.Models;

public sealed record NotificacionResponse(
    int IdNotificacion,
    string Titulo,
    string Mensaje,
    string Tipo,
    string? Folio,
    DateTime FechaHora,
    bool Leida);
