using System.Text.Json;
using System.Threading.RateLimiting;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.CrossCutting.Resources;
using Microsoft.AspNetCore.RateLimiting;

namespace Dsw2026Tpi.Api.Configurations;

/// <summary>
/// Rate limiting nativo de ASP.NET Core (sin paquetes extra). Cuatro politicas Fixed Window cuyos
/// limites salen de appsettings.json. El rechazo comun (429 + sobre de error + log, sin encolar)
/// se resuelve una sola vez en OnRejected.
/// </summary>
public static class RateLimitingConfigurationExtensions
{
    // El sobre de error de la API va en camelCase, igual que el ExceptionHandlingMiddleware.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
                      ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // General (RL-04): el resto de los endpoints, 100/min por usuario autenticado o, si no
            // lo esta, por IP. Como GlobalLimiter aplica a TODO; en los endpoints con politica
            // propia (5 o 10/min) la mas estricta salta primero, asi que este nunca los muerde.
            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var key = context.User.Identity?.IsAuthenticated == true
                    ? context.User.Identity!.Name ?? GetIpKey(context)
                    : GetIpKey(context);

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"general:{key}",
                    _ => CreateFixedWindow(options.General));
            });

            // AdminLogin (RL-01): 5/min por IP. En el login todavia no hay token, la unica
            // identidad disponible es la IP de quien golpea la puerta.
            limiter.AddPolicy(RateLimitPolicies.AdminLogin, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"admin-login:{GetIpKey(context)}",
                    _ => CreateFixedWindow(options.AdminLogin)));

            // PatientLogin (RL-02): 10/min por IP.
            limiter.AddPolicy(RateLimitPolicies.PatientLogin, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    $"patient-login:{GetIpKey(context)}",
                    _ => CreateFixedWindow(options.PatientLogin)));

            // Booking (RL-03): 5/min por paciente autenticado. La reserva ocurre despues del login,
            // asi que particionamos por el claim patientId (fallback a IP por las dudas).
            limiter.AddPolicy(RateLimitPolicies.Booking, context =>
            {
                var key = context.User.FindFirst(CustomClaims.PatientId)?.Value ?? GetIpKey(context);
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"booking:{key}",
                    _ => CreateFixedWindow(options.Booking));
            });

            // Rechazo comun (RL-05/06/07): 429 con el sobre de error del contrato y log del rechazo.
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.ContentType = "application/json";

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                var logger = http.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("RateLimiting");

                logger.LogWarning(
                    "Rate limit excedido para {Method} {Path} desde {Ip}",
                    http.Request.Method, http.Request.Path, GetIpKey(http));

                var error = new ErrorResponse(nameof(ErrorCodes.RATE_LIMIT_EXCEEDED), ErrorCodes.RATE_LIMIT_EXCEEDED);
                await http.Response.WriteAsync(JsonSerializer.Serialize(error, SerializerOptions), cancellationToken);
            };
        });

        return services;
    }

    // Fixed Window con QueueLimit 0 (RL-08): lo que excede el limite se rechaza al instante, no se
    // encola para procesarse cuando se libere la ventana.
    private static FixedWindowRateLimiterOptions CreateFixedWindow(RateLimitPolicyOptions policy) => new()
    {
        PermitLimit = policy.PermitLimit,
        Window = TimeSpan.FromSeconds(policy.WindowSeconds),
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true
    };

    private static string GetIpKey(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
