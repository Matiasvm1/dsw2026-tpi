using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;

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
        var doctors = await _persistence.Paginate<Doctor, string>(pagination.PageSize, pagination.PageIndex,
                                                   d => string.IsNullOrWhiteSpace(name) ||
                                                   d.Name.Contains(name), x => x.Name, nameof(Doctor.Speciality));

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
}
