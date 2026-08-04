namespace Dsw2026Tpi.Api.Configurations;

/// <summary>
/// Mapea la seccion "RateLimiting" de appsettings.json. El TFI exige que los limites se obtengan
/// desde configuracion y no queden hardcodeados en los controladores.
/// </summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitPolicyOptions AdminLogin { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };
    public RateLimitPolicyOptions PatientLogin { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
    public RateLimitPolicyOptions Booking { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };
    public RateLimitPolicyOptions General { get; set; } = new() { PermitLimit = 100, WindowSeconds = 60 };
}

public class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}
