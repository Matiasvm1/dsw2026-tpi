namespace Dsw2026Tpi.Application.Dtos;

public record AppointmentModel
{
    // ---- entrada ----
    public record Request(Guid DoctorId, Guid AvailabilityId, PatientRequest Patient, string Reason);
    public record PatientRequest(long Dni);

    // ---- salida ----
    // Fechas y horas siempre como string con formato explícito: TimeOnly serializa "09:00:00"
    // y arrastramos ese problema desde Fase 1. Formateando a mano, los cinco endpoints devuelven lo mismo.
    public record Response(
        Guid Id,
        string Date,        // "yyyy-MM-dd"
        string StartTime,   // "HH:mm"
        string EndTime,     // "HH:mm"
        string Status,      // BOOKED | CANCELLED | ATTENDED | NO_SHOW
        string Reason,
        DoctorDto Doctor,
        SpecialtyDto Specialty,
        PatientDto Patient);

    // El search del admin tiene un shape propio (contrato): appointmentsId/appointmentsStatus, el
    // DNI como número y la especialidad ANIDADA dentro del médico. Por eso no reutiliza los DTOs de
    // la Response general.
    public record SearchResponse(
        Guid AppointmentsId,
        string AppointmentsStatus,
        SearchPatientDto Patient,
        SearchDoctorDto Doctor,
        string AvailableTime);   // "yyyy-MM-dd HH:mm" (tabla lógica del admin)

    public record SearchPatientDto(long Dni, string? FullName);
    public record SearchDoctorDto(Guid DoctorId, string Name, SearchSpecialtyDto Specialty);
    public record SearchSpecialtyDto(Guid SpecialtyId, string Name);

    public record DoctorDto(Guid Id, string Name);
    public record SpecialtyDto(Guid Id, string Name);
    public record PatientDto(Guid Id, string Dni, string? FullName);   // FullName nullable (D35)
}
