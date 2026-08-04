using System.Security.Claims;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Domain.Interfaces;
using Dsw2026Tpi.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Dsw2026Tpi.Application.Services;

public class AppointmentService : IAppointmentService
{
    private readonly IPersistence _persistence;
    private readonly ILogger<AppointmentService> _logger;

    public AppointmentService(
        IPersistence persistence,
        ILogger<AppointmentService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<AppointmentModel.Response> Book(AppointmentModel.Request request, ClaimsPrincipal user)
    {
        var dniClaim = user.FindFirstValue(CustomClaims.Dni);
        // 2. Validaciones básicas 
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length < 5)
            throw new ValidationException(nameof(ErrorCodes.APPOINTMENT_REASON_LENGTH), ErrorCodes.APPOINTMENT_REASON_LENGTH)
                .WithDetail("reason", "min_length_5");

        var dniString = request.Patient.Dni.ToString(); // Conversión obligatoria para comparar
        if (request.Patient.Dni <= 0 || dniString.Length is < 7 or > 10)
        {
            throw new ValidationException(
                    nameof(ErrorCodes.PATIENT_DNI_INVALID),
                    ErrorCodes.PATIENT_DNI_INVALID)
                .WithDetail("dni", "length_between_7_and_10");
        }

        // 3. Validación de identidad 
        // Si es PACIENTE, su DNI del body tiene que coincidir exactamente con el de su token
        if (user.IsInRole(Roles.Patient) && (dniClaim is null || dniString != dniClaim))
        {
            throw new AuthorizationException(
                nameof(ErrorCodes.APPOINTMENT_FORBIDDEN),
                ErrorCodes.APPOINTMENT_FORBIDDEN);
        }

        // 4. Buscar el paciente por DNI
        var patient = await _persistence.First<Patient>(
            p => p.Dni == dniString)
            ?? throw new EntityNotFoundException(
                nameof(ErrorCodes.PATIENT_NOT_FOUND),
                ErrorCodes.PATIENT_NOT_FOUND);

        // 5. Buscar el médico incluyendo su especialidad
        var doctor = await _persistence.GetById<Doctor>(
            request.DoctorId,
            nameof(Doctor.Speciality))
            ?? throw new EntityNotFoundException(
                nameof(ErrorCodes.DOCTOR_NOT_FOUND),
                ErrorCodes.DOCTOR_NOT_FOUND);

        // 6. Buscar el bloque horario concreto
        var slot = await _persistence.GetById<AvailabilitySlot>(
            request.AvailabilitySlotId)
            ?? throw new EntityNotFoundException(
                nameof(ErrorCodes.APPOINTMENT_SLOT_NOT_FOUND),
                ErrorCodes.APPOINTMENT_SLOT_NOT_FOUND);

        // 7. Verificar que el bloque pertenezca al médico indicado
        if (slot.DoctorId != doctor.Id)
        {
            throw new ValidationException(
                    nameof(ErrorCodes.APPOINTMENT_DOCTOR_MISMATCH),
                    ErrorCodes.APPOINTMENT_DOCTOR_MISMATCH)
                .WithDetail("availabilityId", "does_not_belong_to_doctor");
        }

        // 8. Comprobación previa del estado del bloque
        if (slot.Status != SlotStatus.Available)
        {
            throw new BusinessRuleException(
                    nameof(ErrorCodes.APPOINTMENT_SLOT_UNAVAILABLE),
                    ErrorCodes.APPOINTMENT_SLOT_UNAVAILABLE)
                .WithDetail("availabilityId", "slot_not_available");
        }

        // 9. RN04: el turno debe comenzar en el futuro
        var slotDateTime = slot.SlotDate.ToDateTime(slot.StartTime);

        if (slotDateTime <= DateTime.UtcNow)
        {
            throw new ValidationException(
                    nameof(ErrorCodes.APPOINTMENT_IN_THE_PAST),
                    ErrorCodes.APPOINTMENT_IN_THE_PAST)
                .WithDetail("availabilityId", "slot_must_be_in_the_future");
        }

        // 10. Crear e insertar el turno
        var appointment = new Appointment(
            slot.Id,
            patient.Id,
            request.Reason.Trim());

        try
        {
            await _persistence.Add(appointment);
        }
        catch (ConflictException)
        {
            _logger.LogWarning(
                "Conflicto de reserva sobre el slot {SlotId}",
                slot.Id);

            throw new BusinessRuleException(
                    nameof(ErrorCodes.APPOINTMENT_CONFLICT),
                    ErrorCodes.APPOINTMENT_CONFLICT)
                .WithDetail("availabilityId", "slot_already_booked");
        }

        // 11. Solo quien ganó el INSERT marca el slot como reservado
        slot.Book();
        await _persistence.Update(slot);

        // 12. Registrar la operación
        _logger.LogInformation(
            "Turno {AppointmentId} reservado para el paciente {PatientId} en el slot {SlotId}",
            appointment.Id,
            patient.Id,
            slot.Id);

        // 13. Construir la respuesta con los objetos ya consultados
        var specialty = doctor.Speciality!;

        return new AppointmentModel.Response(
            appointment.Id,
            slot.SlotDate.ToString("yyyy-MM-dd"),
            slot.StartTime.ToString("HH\\:mm"),
            slot.EndTime.ToString("HH\\:mm"),
            "BOOKED",
            appointment.Reason,
            new AppointmentModel.DoctorDto(
                doctor.Id,
                doctor.Name),
            new AppointmentModel.SpecialtyDto(
                specialty.Id,
                specialty.Name),
            new AppointmentModel.PatientDto(
                patient.Id,
                patient.Dni,
                patient.FullName));



    }
    public async Task Cancel(Guid appointmentId, ClaimsPrincipal user)
    {
        // 1. Extraemos los claims
        var patientIdClaim = user.FindFirstValue(CustomClaims.PatientId);

        // 2. Buscar el Appointment con el include del slot 
        var appointment = await _persistence.GetById<Appointment>(appointmentId, nameof(Appointment.AvailabilitySlot));
        
        if (appointment is null)
            throw new EntityNotFoundException(nameof(ErrorCodes.APPOINTMENT_NOT_FOUND), ErrorCodes.APPOINTMENT_NOT_FOUND);

        // 3. Validación de identidad
        // Si es PACIENTE, el turno debe pertenecer a su ID. Se compara contra el rol interno
        // (Roles.Patient = "Paciente"), igual que Book(): el claim lleva el nombre interno, no el
        // de respuesta del contrato. Y un turno ajeno es un 403 (AuthorizationException), no un 409.
        if (user.IsInRole(Roles.Patient))
        {
            if (patientIdClaim == null || appointment.PatientId.ToString() != patientIdClaim)
                throw new AuthorizationException(nameof(ErrorCodes.APPOINTMENT_FORBIDDEN), ErrorCodes.APPOINTMENT_FORBIDDEN);
        }

        // 4. El estado debe ser BOOKED 
        if (appointment.Status != AppointmentStatus.Booked)
            throw new BusinessRuleException(nameof(ErrorCodes.APPOINTMENT_NOT_CANCELLABLE), ErrorCodes.APPOINTMENT_NOT_CANCELLABLE);

        // 5. Cambios de estado 
        appointment.Cancel(); // Status = CANCELLED + CancelledAt
        appointment.AvailabilitySlot!.Release(); // El slot vuelve a AVAILABLE

        // 6. Guardar en base de datos 
        await _persistence.Update(appointment);
        await _persistence.Update(appointment.AvailabilitySlot);

        // 7. Loguear la cancelación
        _logger.LogInformation("El turno {AppointmentId} fue cancelado exitosamente.", appointmentId);
    }
}
