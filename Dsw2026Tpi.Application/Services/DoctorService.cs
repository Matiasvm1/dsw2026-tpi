using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.CrossCutting.Helpers;


namespace Dsw2026Tpi.Application.Services;

public class DoctorService : IDoctorService
{
    private readonly IPersistence _persistence;

    public DoctorService(IPersistence persistence)
    {
        _persistence = persistence;
    }

    // Implementación de referencia del GET paginado con filtro por nombre: los cuatro módulos
    // copian esta forma. Sin filtro no se pasa null, se pasa un predicado que siempre da true.
    // Contains va pelado, sin StringComparison: EF no lo traduce a SQL y falla en ejecución.
    // El case-insensitive lo aporta la collation de SQL Server.
    public async Task<Pagination<DoctorModel.Response>> GetAll(PaginationQuery pagination, string? name = null)
    {
        if (name is not null &&
    (string.IsNullOrWhiteSpace(name) ||
     name.Trim().Length is < 3 or > 100))
        {
            throw new ValidationException(
                    nameof(ErrorCodes.DOCTOR_NAME_LENGTH),
                    ErrorCodes.DOCTOR_NAME_LENGTH)
                .WithDetail(
                    nameof(name),
                    "length_between_3_and_100");
        }

        var normalizedName = name?.Trim();

        var doctors = await _persistence.Paginate<Doctor, string>(pagination.PageSize, pagination.PageIndex,
                                                   d => normalizedName == null ||
     d.Name.Contains(normalizedName), x => x.Name, nameof(Doctor.Speciality));

        return doctors.Map(d => new DoctorModel.Response(
     d.Id,
     d.Name,
     d.LicenseNumber,
     d.Speciality is null
         ? null
         : new DoctorModel.SpecialityDto(
             d.Speciality.Id,
             d.Speciality.Name)));
    }

    public async Task<DoctorModel.Response> Create(
    DoctorModel.Request request)
    {
        Validate(request);

        var speciality = await _persistence.GetById<Speciality>(
            request.SpecialtyId);

        if (speciality is null)
        {
            throw new ValidationException(
                    nameof(ErrorCodes.DOCTOR_SPECIALTY_NOT_FOUND),
                    ErrorCodes.DOCTOR_SPECIALTY_NOT_FOUND)
                .WithDetail(
                    nameof(request.SpecialtyId),
                    "not_found");
        }

        var doctor = new Doctor(
            request.Name.Trim(),
            request.LicenseNumber?.Trim(),
            request.SpecialtyId);

        await _persistence.Add(doctor);

        return new DoctorModel.Response(
            doctor.Id,
            doctor.Name,
            doctor.LicenseNumber,
            new DoctorModel.SpecialityDto(
                speciality.Id,
                speciality.Name));
    }

    public async Task<DoctorModel.Response> Update(
    Guid id,
    DoctorModel.Request request)
    {
        Validate(request);

        var doctor = await _persistence.GetById<Doctor>(id)
            ?? throw new EntityNotFoundException(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ErrorCodes.DOCTOR_NOT_FOUND);

        var speciality = await _persistence.GetById<Speciality>(
            request.SpecialtyId);

        if (speciality is null)
        {
            throw new ValidationException(
                    nameof(ErrorCodes.DOCTOR_SPECIALTY_NOT_FOUND),
                    ErrorCodes.DOCTOR_SPECIALTY_NOT_FOUND)
                .WithDetail(
                    nameof(request.SpecialtyId),
                    "not_found");
        }

        doctor.Update(
            request.Name.Trim(),
            request.LicenseNumber?.Trim(),
            request.SpecialtyId);

        await _persistence.Update(doctor);

        return new DoctorModel.Response(
            doctor.Id,
            doctor.Name,
            doctor.LicenseNumber,
            new DoctorModel.SpecialityDto(
                speciality.Id,
                speciality.Name));
    }

    public async Task Delete(Guid id)
    {
        var doctor = await _persistence.GetById<Doctor>(id)
            ?? throw new EntityNotFoundException(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ErrorCodes.DOCTOR_NOT_FOUND);

        await _persistence.Delete(doctor);
    }


    public async Task<IEnumerable<DoctorModel.AvailabilityResponse>>
    GetAvailabilities(Guid doctorId)
    {
        var doctor = await _persistence.GetById<Doctor>(doctorId)
            ?? throw new EntityNotFoundException(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ErrorCodes.DOCTOR_NOT_FOUND);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var rules = await _persistence.GetFiltered<AvailabilityRule>(
            rule =>
                rule.DoctorId == doctorId &&
                rule.Year == today.Year &&
                rule.Month == today.Month);

        return rules!
            .OrderBy(rule =>
                rule.DayOfWeek == DayOfWeek.Sunday
                    ? 7
                    : (int)rule.DayOfWeek)
            .ThenBy(rule => rule.StartTime)
            .Select(rule => new DoctorModel.AvailabilityResponse(
                rule.Id,
                DayOfWeekMapper.ToSpanish(rule.DayOfWeek),
                rule.StartTime.ToString("HH\\:mm"),
                rule.EndTime.ToString("HH\\:mm")));
    }

    private const int MaxSlotRangeDays = 31;
    private const int DefaultSlotRangeDays = 7;

    // CU01 paso 4: "el sistema muestra horarios disponibles". POST /api/appointments pide un
    // availabilitySlotId, pero GET /availabilities devuelve reglas ("Lunes 09:00-12:00"), no los
    // turnos concretos: sin este endpoint el front no tiene de dónde sacar ese id.
    // Devuelve también los reservados y bloqueados con su estado: el mockup mobile los muestra
    // tachados (CU01 4b, "no están disponibles para elección") en vez de ocultarlos.
    public async Task<IEnumerable<DoctorModel.SlotResponse>> GetSlots(Guid doctorId, string? from, string? to)
    {
        _ = await _persistence.GetById<Doctor>(doctorId)
            ?? throw new EntityNotFoundException(nameof(ErrorCodes.DOCTOR_NOT_FOUND), ErrorCodes.DOCTOR_NOT_FOUND);

        var nowUtc = DateTime.UtcNow;
        var fromDate = ParseSlotDate(from, nameof(from)) ?? DateOnly.FromDateTime(nowUtc);
        var toDate = ParseSlotDate(to, nameof(to)) ?? fromDate.AddDays(DefaultSlotRangeDays - 1);

        if (toDate < fromDate)
        {
            throw new ValidationException(nameof(ErrorCodes.SLOT_RANGE_INVALID), ErrorCodes.SLOT_RANGE_INVALID)
                .WithDetail(nameof(to), "must_not_be_before_from");
        }

        // Tope de 31 días: alcanza para una vista mensual y evita traer miles de filas de golpe.
        if (toDate.DayNumber - fromDate.DayNumber + 1 > MaxSlotRangeDays)
        {
            throw new ValidationException(nameof(ErrorCodes.SLOT_RANGE_INVALID), ErrorCodes.SLOT_RANGE_INVALID)
                .WithDetail(nameof(to), "range_max_31_days");
        }

        var slots = await _persistence.GetFiltered<AvailabilitySlot>(
            slot =>
                slot.DoctorId == doctorId &&
                slot.SlotDate >= fromDate &&
                slot.SlotDate <= toDate);

        // RN04: lo pasado no se puede reservar, así que ni se muestra. Se filtra en memoria porque
        // combinar SlotDate + StartTime en una fecha no lo traduce EF a SQL. Mismo criterio de
        // "ahora" (UTC) que la generación de slots y la reserva.
        return (slots ?? [])
            .Where(slot => slot.SlotDate.ToDateTime(slot.StartTime) > nowUtc)
            .OrderBy(slot => slot.SlotDate)
            .ThenBy(slot => slot.StartTime)
            .Select(slot => new DoctorModel.SlotResponse(
                slot.Id,
                slot.SlotDate.ToString("yyyy-MM-dd"),
                slot.StartTime.ToString("HH\\:mm"),
                slot.EndTime.ToString("HH\\:mm"),
                slot.Status.ToString().ToUpperInvariant()));
    }

    // Se reciben como string (no DateOnly?) por el mismo motivo que en las consultas de turnos:
    // con DateOnly? el framework respondería su propio 400 y nuestro código de error no saldría.
    private static DateOnly? ParseSlotDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", out var parsed))
        {
            throw new ValidationException(nameof(ErrorCodes.SLOT_RANGE_INVALID), ErrorCodes.SLOT_RANGE_INVALID)
                .WithDetail(field, "expected_format_yyyy-MM-dd");
        }

        return parsed;
    }


    private static void Validate(DoctorModel.Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length is < 3 or > 100)
        {
            throw new ValidationException(
                    nameof(ErrorCodes.DOCTOR_NAME_LENGTH),
                    ErrorCodes.DOCTOR_NAME_LENGTH)
                .WithDetail(
                    nameof(request.Name),
                    "length_between_3_and_100");
        }
    }


}
