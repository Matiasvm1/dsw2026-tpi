using System.Security.Claims;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.Domain.Entities;

namespace Dsw2026Tpi.Application.Interfaces;

/// <summary>
/// Consultas de solo lectura del módulo de turnos (D21: separado de los comandos).
/// </summary>
public interface IAppointmentQueryService
{
    // D20: el ClaimsPrincipal dice QUIÉN pide; el dni del query dice QUÉ se quiere. El service
    // compara los dos para decidir el 403, no el controller.
    Task<IEnumerable<AppointmentModel.Response>> GetByPatient(string dni, ClaimsPrincipal user);

    Task<Pagination<AppointmentModel.Response>> GetByDate(string? date, PaginationQuery pagination);

    Task<Pagination<AppointmentModel.SearchResponse>> Search(
        Guid? specialtyId, Guid? doctorId, string? dni, string? date, PaginationQuery pagination);
}
