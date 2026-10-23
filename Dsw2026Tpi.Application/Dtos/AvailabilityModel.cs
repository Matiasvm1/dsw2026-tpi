namespace Dsw2026Tpi.Application.Dtos;

public record AvailabilityModel
{
    // Year y Month son opcionales y van juntos: sin ellos se usa el mes actual (contrato original);
    // con ellos el admin puede cargar la agenda del mes siguiente antes de que empiece.
    public record Request(Guid DoctorId, IReadOnlyList<DayRequest> Days, int? Year = null, int? Month = null);
    public record DayRequest(string Day, string StartTime, string EndTime);
    // El TFI pide "retornar la nueva entidad creada / actualizada": devolvemos el schedule en
    // efecto (los días con su horario), no un conteo.
    public record Response(Guid DoctorId, int Year, int Month, IReadOnlyList<DayResponse> Days);
    public record DayResponse(string Day, string StartTime, string EndTime);
    // PUT /api/availabilities/slots/{id}/status: bloqueo manual de un horario (BLOCKED | AVAILABLE).
    public record SlotStatusRequest(string Status);
}