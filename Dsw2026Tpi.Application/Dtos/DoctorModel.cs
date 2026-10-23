namespace Dsw2026Tpi.Application.Dtos;

public record DoctorModel
{
    // Todo lo que cruza el cable va en americano (specialty): el PDF nunca escribe "speciality"
    // en británico. Request -> "specialtyId" (p. 15); respuesta -> "specialty" (p. 16).
    // El británico queda SOLO en los nombres de clases/entidades, que no se serializan.
    public record Request(string Name, string? LicenseNumber, Guid SpecialtyId);
    // HasAvailability no está en el contrato (p. 16): es un agregado para la columna "Estado de
    // disponibilidad" del front. Se calcula siempre a partir de los slots, nunca se guarda.
    public record Response(Guid Id, string Name, string? LicenseNumber, SpecialityDto? Specialty, bool HasAvailability);
    public record SpecialityDto(Guid Id, string Name);
    public record AvailabilityResponse(Guid Id, string Day, string StartTime, string EndTime);

    // Un turno de 30 minutos concreto. El Id es el availabilitySlotId que pide POST /api/appointments.
    public record SlotResponse(
        Guid Id,
        string Date,        // "yyyy-MM-dd"
        string StartTime,   // "HH:mm"
        string EndTime,     // "HH:mm"
        string Status);     // AVAILABLE | BOOKED | BLOCKED

  
}
