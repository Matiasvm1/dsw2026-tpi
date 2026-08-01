namespace Dsw2026Tpi.CrossCutting.Identity;

/// <summary>
/// Claims propios que viajan en el token del paciente. Fase 2 los lee para resolver el turno sin
/// volver a la base, así que el nombre se declara una sola vez y de los dos lados sale de acá.
/// </summary>
public class CustomClaims
{
    public const string PatientId = "patientId";
    public const string Dni = "dni";
}
