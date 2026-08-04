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
