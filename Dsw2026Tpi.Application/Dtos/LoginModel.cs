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
    public record Response(string? Token, string? Role);
}
