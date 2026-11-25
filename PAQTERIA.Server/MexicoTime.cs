namespace PAQTERIA.Server;

public static class MexicoTime
{
    private static readonly TimeZoneInfo HermosilloTimeZone = ResolveHermosilloTimeZone();

    public static DateOnly Today
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, HermosilloTimeZone));

    private static TimeZoneInfo ResolveHermosilloTimeZone()
    {
        var ids = OperatingSystem.IsWindows()
            ? new[] { "US Mountain Standard Time", "America/Hermosillo" }
            : new[] { "America/Hermosillo", "US Mountain Standard Time" };

        foreach (var id in ids)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the equivalent Windows/IANA identifier.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the equivalent Windows/IANA identifier.
            }
        }

        throw new TimeZoneNotFoundException("No se encontró la zona horaria de Hermosillo.");
    }
}
