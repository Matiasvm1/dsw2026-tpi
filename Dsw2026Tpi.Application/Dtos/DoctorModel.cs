namespace Dsw2026Tpi.Application.Dtos;

public record DoctorModel
{
    // Todo lo que cruza el cable va en americano (specialty): el PDF nunca escribe "speciality"
    // en británico. Request -> "specialtyId" (p. 15); respuesta -> "specialty" (p. 16).
    // El británico queda SOLO en los nombres de clases/entidades, que no se serializan.
    public record Request(string Name, string? LicenseNumber, Guid SpecialtyId);
    public record Response(Guid Id, string Name, string? LicenseNumber, SpecialityDto? Specialty);
    public record SpecialityDto(Guid Id, string Name);
    public record AvailabilityResponse(Guid Id, string Day, string StartTime, string EndTime);

  
}
