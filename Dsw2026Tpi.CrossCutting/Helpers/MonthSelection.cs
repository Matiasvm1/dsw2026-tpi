using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Resources;

namespace Dsw2026Tpi.CrossCutting.Helpers;

/// <summary>
/// Resuelve el mes sobre el que opera la disponibilidad. La disponibilidad es mensual (TFI p. 17):
/// sin year ni month se usa el mes actual, que es el comportamiento original del contrato.
/// </summary>
public static class MonthSelection
{
    public static (int Year, int Month) Resolve(int? year, int? month, DateTime nowUtc)
    {
        if (year is null && month is null) return (nowUtc.Year, nowUtc.Month);

        if (year is null || month is null)
            throw Invalid("year_and_month_required_together");

        if (month is < 1 or > 12)
            throw Invalid("month_between_1_and_12");

        return (year.Value, month.Value);
    }

    // Cuántos meses separan (year, month) del mes actual: 0 = este mes, 1 = el siguiente, -1 = el anterior.
    public static int MonthsFromNow(int year, int month, DateTime nowUtc) =>
        (year * 12 + month) - (nowUtc.Year * 12 + nowUtc.Month);

    public static AppException Invalid(string issue) =>
        new ValidationException(nameof(ErrorCodes.AVAILABILITY_MONTH_INVALID), ErrorCodes.AVAILABILITY_MONTH_INVALID)
            .WithDetail("month", issue);
}
