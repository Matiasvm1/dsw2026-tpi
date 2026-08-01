namespace Dsw2026Tpi.CrossCutting.Identity;

public class Roles
{
    // Nombre interno del rol: es el que está sembrado en la base desde roles.json, el que viaja
    // en el claim del token y el que evalúan las policies. No cambiar a mayúsculas.
    public const string Administrator = "Administrador";
    public const string Patient = "Paciente";

    // Nombre que exige el contrato en la respuesta del login. Es otra cosa que el nombre interno.
    public const string AdministratorResponse = "ADMINISTRADOR";
    public const string PatientResponse = "PACIENTE";

    public static string ToResponse(string? role) => role switch
    {
        Administrator => AdministratorResponse,
        Patient => PatientResponse,
        _ => string.Empty
    };
}
