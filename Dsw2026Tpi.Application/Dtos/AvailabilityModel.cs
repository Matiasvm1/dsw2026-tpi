namespace Dsw2026Tpi.Application.Dtos;

public record AvailabilityModel
{
    public record Request(Guid DoctorId, IReadOnlyList<DayRequest> Days);
    public record DayRequest(string Day, string StartTime, string EndTime);
    // El TFI pide "retornar la nueva entidad creada / actualizada": devolvemos el schedule en
    // efecto (los días con su horario), no un conteo.
    public record Response(Guid DoctorId, int Year, int Month, IReadOnlyList<DayResponse> Days);
    public record DayResponse(string Day, string StartTime, string EndTime);
}