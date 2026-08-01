namespace Dsw2026Tpi.Application.Dtos;

public record AvailabilityModel
{
    public record Request(Guid DoctorId, IReadOnlyList<DayRequest> Days);
    public record DayRequest(string Day, string StartTime, string EndTime);
    public record Response(Guid DoctorId, int Year, int Month, int RulesCreated, int SlotsCreated);
}