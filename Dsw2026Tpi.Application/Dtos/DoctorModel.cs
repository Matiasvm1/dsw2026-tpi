namespace Dsw2026Tpi.Application.Dtos;

public record DoctorModel
{
    // El contrato mezcla los dos idiomas a propósito (PDF p. 15) y hay que respetarlo tal cual:
    // el request pide specialityId (británico) y la respuesta anida specialty (americano).
    public record Request(string Name, string? LicenseNumber, Guid SpecialityId);
    public record Response(Guid Id, string Name, string? LicenseNumber, SpecialityDto? Specialty);
    public record SpecialityDto(Guid? Id, string? Name);
}
