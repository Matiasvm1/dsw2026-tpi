namespace Dsw2026Tpi.Application.Dtos;

public record LoginAdminModel
{
    public record Request(string Email, string Password);
    public record Response(string? Token, string? Role);
}

public record LoginPatientModel
{
    // FullName es opcional (no está en el contrato, p. 13): sin él, el login funciona igual que antes.
    public record Request(string Email, long Dni, string? FullName = null);
    // Dni y PatientId se agregan a token y role (p. 13) para que el front no tenga que decodificar
    // el JWT: los necesita para "Mis turnos" (?dni=) y para reservar (patient.dni).
    public record Response(string? Token, string? Role, long Dni, Guid PatientId);
}
