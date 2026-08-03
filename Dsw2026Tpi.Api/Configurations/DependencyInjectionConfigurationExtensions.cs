using Dsw2026Tpi.Api.Services;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.Application.Services;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Data;
using Dsw2026Tpi.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Configurations;

public static class DependencyInjectionConfigurationExtensions
{
    public static IServiceCollection AddAppDependencies(this IServiceCollection services)
    {
        services.AddScoped<IPersistence, PersistenceEf>();
        services.AddScoped<IDoctorService, DoctorService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<ISignInService, SignInService>();
        services.AddSingleton<JwtService>();
        services.AddScoped<ISpecialityService, SpecialityService>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IAppointmentQueryService, AppointmentQueryService>();
        return services;
    }

    /// <summary>
    /// Registra los controladores y hace que los 400 automáticos de [ApiController]
    /// (JSON malformado, un campo que no bindea) salgan con el sobre de error del contrato
    /// { errorCode, message, details } en lugar del ValidationProblemDetails de ASP.NET.
    /// </summary>
    public static IServiceCollection AddAppControllers(this IServiceCollection services)
    {
        services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var error = new ErrorResponse(nameof(ErrorCodes.VALIDATION_ERROR),
                        ErrorCodes.VALIDATION_ERROR);

                    foreach (var (field, state) in context.ModelState)
                    {
                        foreach (var e in state.Errors)
                        {
                            error.AddDetail(field, string.IsNullOrWhiteSpace(e.ErrorMessage)
                                ? "invalid_value" : e.ErrorMessage);
                        }
                    }

                    // BadRequestObjectResult serializa con MVC, o sea en camelCase, igual que el
                    // ExceptionHandlingMiddleware. Un solo formato de error en toda la API.
                    return new BadRequestObjectResult(error);
                };
            });

        return services;
    }
}
