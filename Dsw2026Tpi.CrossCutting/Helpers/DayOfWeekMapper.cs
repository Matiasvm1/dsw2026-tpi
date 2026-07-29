namespace Dsw2026Tpi.CrossCutting.Helpers;

public static class DayOfWeekMapper
{
    // Salida CON tilde, tal cual la enumeración literal del PDF, p.16 (D13 rev.)
    private static readonly Dictionary<DayOfWeek, string> ToSpanishMap = new()
    {
        [DayOfWeek.Monday]    = "LUNES",
        [DayOfWeek.Tuesday]   = "MARTES",
        [DayOfWeek.Wednesday] = "MIÉRCOLES",
        [DayOfWeek.Thursday]  = "JUEVES",
        [DayOfWeek.Friday]    = "VIERNES",
        [DayOfWeek.Saturday]  = "SÁBADO",
        [DayOfWeek.Sunday]    = "DOMINGO"
    };

    // Las claves de entrada se normalizan SIN tilde, así "MIERCOLES" y "MIÉRCOLES" matchean igual
    private static readonly Dictionary<string, DayOfWeek> FromSpanishMap =
        ToSpanishMap.ToDictionary(p => Normalize(p.Value), p => p.Key);

    public static string ToSpanish(DayOfWeek day) => ToSpanishMap[day];

    public static bool TryParse(string? value, out DayOfWeek day)
    {
        day = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return FromSpanishMap.TryGetValue(Normalize(value), out day);
    }

    // Acepta "Miércoles", "miercoles", "MIÉRCOLES" y los normaliza a "MIERCOLES"
    private static string Normalize(string value) =>
        new string(value.Trim().ToUpperInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
}