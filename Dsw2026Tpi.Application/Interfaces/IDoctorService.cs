using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.Domain.Entities;

namespace Dsw2026Tpi.Application.Interfaces;

public interface IDoctorService
{
    Task<Pagination<DoctorModel.Response>> GetAll(PaginationQuery pagination, string? name = null, Guid? specialtyId = null);

    Task<DoctorModel.Response> GetById(Guid id);

    Task<DoctorModel.Response> Create(DoctorModel.Request request);

    Task<DoctorModel.Response> Update(
        Guid id,
        DoctorModel.Request request);

    Task Delete(Guid id);

    Task<IEnumerable<DoctorModel.AvailabilityResponse>> GetAvailabilities(
        Guid doctorId);

    Task<IEnumerable<DoctorModel.SlotResponse>> GetSlots(
        Guid doctorId, string? from, string? to);
}
