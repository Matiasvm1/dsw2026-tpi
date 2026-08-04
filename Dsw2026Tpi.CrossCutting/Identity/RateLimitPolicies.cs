namespace Dsw2026Tpi.CrossCutting.Identity;

/// <summary>
/// Nombres de las politicas de rate limiting. Viven en la capa transversal para que el arranque
/// (que las define) y los controllers (que las aplican con [EnableRateLimiting]) usen la misma
/// constante y no un string suelto propenso a typos.
/// </summary>
public static class RateLimitPolicies
{
    public const string AdminLogin = "AdminLogin";
    public const string PatientLogin = "PatientLogin";
    public const string Booking = "Booking";
}
