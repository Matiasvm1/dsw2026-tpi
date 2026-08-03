using System.Security.Claims;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Domain.Interfaces;

namespace Dsw2026Tpi.Application.Services;

public class AppointmentService : IAppointmentService
{
    private readonly IPersistence _persistence;

    public AppointmentService(IPersistence persistence) => _persistence = persistence;

    public async Task<AppointmentModel.Response> Book(AppointmentModel.Request request, ClaimsPrincipal user)
    {
        // 1. Extraemos los claims
        var role = user.FindFirstValue(ClaimTypes.Role);
        var dniClaim = user.FindFirstValue("dni");

        // 2. Validaciones básicas (Pasos 1 y 2 del documento)
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw new ValidationException(nameof(ErrorCodes.APPOINTMENT_REASON_LENGTH), ErrorCodes.APPOINTMENT_REASON_LENGTH)
                .WithDetail("reason", "min_length_5");

        var dniString = request.Patient.Dni.ToString(); // Conversión obligatoria para comparar
        if (dniString.Length is < 7 or > 10)
            throw new ValidationException(nameof(ErrorCodes.PATIENT_DNI_INVALID), ErrorCodes.PATIENT_DNI_INVALID)
                .WithDetail("dni", "length_between_7_and_10");

        // 3. Validación de identidad (Paso 3)
        // Si es PACIENTE, su DNI del body tiene que coincidir exactamente con el de su token
        if (role == Roles.PatientResponse && dniString != dniClaim)
        {
            throw new ForbiddenException(nameof(ErrorCodes.APPOINTMENT_FORBIDDEN), 
                ErrorCodes.APPOINTMENT_FORBIDDEN);
        }

        
    }
}