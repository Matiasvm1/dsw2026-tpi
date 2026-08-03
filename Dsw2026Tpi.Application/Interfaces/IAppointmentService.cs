using System.Security.Claims;
using Dsw2026Tpi.Application.Dtos;

namespace Dsw2026Tpi.Application.Interfaces;

public interface IAppointmentService
{
    Task<AppointmentModel.Response> Book(
        AppointmentModel.Request request,
        ClaimsPrincipal user);

    Task Cancel(Guid appointmentId, ClaimsPrincipal user);
}