using Dsw2026Tpi.Application.Dtos;
public interface IAppointmentService
{
    Task<AppointmentModel.Response> Book(AppointmentModel.Request request, ClaimsPrincipal user);
    Task Cancel(Guid appointmentId, ClaimsPrincipal user);
}