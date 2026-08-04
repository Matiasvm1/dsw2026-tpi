using System.Security.Claims;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;

namespace Dsw2026Tpi.Application.Services;

public class AppointmentQueryService : IAppointmentQueryService
{
    // El turno no guarda ni la fecha ni el médico: todo cuelga del slot. Sin este include, Doctor
    // y Speciality vuelven null y el JSON sale incompleto. La cadena se escribe con puntos.
    private static readonly string[] FullGraph =
    [
        "AvailabilitySlot.AvailabilityRule.Doctor.Speciality",
        "Patient"
    ];

    private readonly IPersistence _persistence;

    public AppointmentQueryService(IPersistence persistence)
    {
        _persistence = persistence;
    }

    // GET /api/appointments/patient?dni=  ·  solo turnos activos (BOOKED), array plano sin sobre.
    public async Task<IEnumerable<AppointmentModel.Response>> GetByPatient(string dni, ClaimsPrincipal user)
    {
        // D20: un paciente solo puede ver lo suyo. El claim "dni" es la verdad de quién pide; el
        // admin no lleva ese claim y no tiene restricción.
        if (user.IsInRole(Roles.Patient))
        {
            var ownDni = user.FindFirst(CustomClaims.Dni)?.Value;
            if (ownDni is null || ownDni != dni)
                throw new AuthorizationException(
                    nameof(ErrorCodes.APPOINTMENT_FORBIDDEN),
                    ErrorCodes.APPOINTMENT_FORBIDDEN);
        }

        var appointments = await _persistence.GetFiltered<Appointment>(
            a => a.Status == AppointmentStatus.Booked && a.Patient!.Dni == dni,
            FullGraph);

        // DNI sin turnos -> [] con 200 (GetFiltered puede devolver null si no hay coincidencias).
        return (appointments ?? [])
            .OrderBy(a => a.AvailabilitySlot?.SlotDate)
            .ThenBy(a => a.AvailabilitySlot?.StartTime)
            .Select(MapToResponse);
    }

    // GET /api/appointments?date=  ·  admin-only, todos los estados, ordenado por hora de inicio.
    public async Task<Pagination<AppointmentModel.Response>> GetByDate(string? date, PaginationQuery pagination)
    {
        // Sin date se usa el día de hoy; con date mal formado sale 400 APPOINTMENT_DATE_INVALID.
        var day = ParseOptionalDate(date) ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await _persistence.Paginate<Appointment, TimeOnly>(
            pagination.PageSize,
            pagination.PageIndex,
            a => a.AvailabilitySlot!.SlotDate == day,
            a => a.AvailabilitySlot!.StartTime,
            FullGraph);

        return result.Map(MapToResponse);
    }

    // GET /api/appointments/search  ·  admin-only, cuatro filtros opcionales y combinables.
    public async Task<Pagination<AppointmentModel.SearchResponse>> Search(
        Guid? specialtyId, Guid? doctorId, string? dni, string? date, PaginationQuery pagination)
    {
        var parsedDate = ParseOptionalDate(date);

        // Patrón "filtro == null || condición", igual que DoctorService.GetAll: EF descarta la
        // condición nula al traducir a SQL. No se arma el IQueryable a mano con ifs.
        var result = await _persistence.Paginate<Appointment, DateOnly>(
            pagination.PageSize,
            pagination.PageIndex,
            a => (specialtyId == null || a.AvailabilitySlot!.AvailabilityRule!.Doctor!.SpecialityId == specialtyId)
              && (doctorId    == null || a.AvailabilitySlot!.AvailabilityRule!.DoctorId == doctorId)
              && (dni         == null || a.Patient!.Dni == dni)
              && (parsedDate  == null || a.AvailabilitySlot!.SlotDate == parsedDate),
            a => a.AvailabilitySlot!.SlotDate,
            FullGraph);

        return result.Map(MapToSearchResponse);
    }

    // Parsea date solo si viene; formato inválido -> 400 APPOINTMENT_DATE_INVALID. Se recibe como
    // string (no DateOnly?) a propósito: si bindeara DateOnly? el 400 automático del framework se
    // dispararía antes de llegar acá y nuestro código de error nunca saldría.
    private static DateOnly? ParseOptionalDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed))
            throw new ValidationException(
                    nameof(ErrorCodes.APPOINTMENT_DATE_INVALID),
                    ErrorCodes.APPOINTMENT_DATE_INVALID)
                .WithDetail("date", "expected_format_yyyy-MM-dd");

        return parsed;
    }

    // Mapeo defensivo (§0.2): el slot puede llegar null aunque el include esté bien escrito, si el
    // AvailabilitySlot quedó dado de baja por un PUT posterior (pasa con los cancelados que ?date=
    // sí devuelve). Todo con ?. y valores por defecto para no romper.
    private static AppointmentModel.Response MapToResponse(Appointment a)
    {
        var slot = a.AvailabilitySlot;
        var doctor = slot?.AvailabilityRule?.Doctor;
        var speciality = doctor?.Speciality;
        var patient = a.Patient;

        return new AppointmentModel.Response(
            a.Id,
            slot?.SlotDate.ToString("yyyy-MM-dd") ?? string.Empty,
            slot?.StartTime.ToString("HH\\:mm") ?? string.Empty,
            slot?.EndTime.ToString("HH\\:mm") ?? string.Empty,
            StatusToResponse(a.Status),
            a.Reason,
            new AppointmentModel.DoctorDto(doctor?.Id ?? Guid.Empty, doctor?.Name ?? string.Empty),
            new AppointmentModel.SpecialtyDto(speciality?.Id ?? Guid.Empty, speciality?.Name ?? string.Empty),
            new AppointmentModel.PatientDto(patient?.Id ?? Guid.Empty, patient?.Dni ?? string.Empty, patient?.FullName));
    }

    private static AppointmentModel.SearchResponse MapToSearchResponse(Appointment a)
    {
        var slot = a.AvailabilitySlot;
        var doctor = slot?.AvailabilityRule?.Doctor;
        var speciality = doctor?.Speciality;
        var patient = a.Patient;

        var availableTime = slot is null
            ? string.Empty
            : $"{slot.SlotDate:yyyy-MM-dd} {slot.StartTime:HH\\:mm}";

        // El contrato del search pide el DNI como número; Patient.Dni se guarda como string.
        var dni = long.TryParse(patient?.Dni, out var parsedDni) ? parsedDni : 0;

        return new AppointmentModel.SearchResponse(
            a.Id,
            StatusToResponse(a.Status),
            new AppointmentModel.SearchPatientDto(dni, patient?.FullName),
            new AppointmentModel.SearchDoctorDto(
                doctor?.Id ?? Guid.Empty,
                doctor?.Name ?? string.Empty,
                new AppointmentModel.SearchSpecialtyDto(speciality?.Id ?? Guid.Empty, speciality?.Name ?? string.Empty)),
            availableTime);
    }

    // El enum del dominio no coincide 1:1 con el contrato (NoShow -> NO_SHOW), así que se mapea a mano.
    private static string StatusToResponse(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Booked => "BOOKED",
        AppointmentStatus.Cancelled => "CANCELLED",
        AppointmentStatus.Attended => "ATTENDED",
        AppointmentStatus.NoShow => "NO_SHOW",
        _ => status.ToString().ToUpperInvariant()
    };
}
