using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dsw2026Tpi.Application.Dtos;
namespace Dsw2026Tpi.Api.Controllers;

[Route("api/appointments")]
[Authorize] // Paciente o Administrador
public class AppointmentController : AppController
{
    private readonly IAppointmentService _service;
    public AppointmentController(IAppointmentService service) => _service = service;
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Book([FromBody] AppointmentModel.Request request)
    {
        // Pasamos el request y los claims al servicio
        var response = await _service.Book(request, User);
        
        return Created($"/api/appointments/{response.Id}", response);
    }
}