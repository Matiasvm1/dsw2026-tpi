using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Identity;
namespace Dsw2026Tpi.Api.Controllers;

[Route("api/appointments")]
[Authorize] // Paciente o Administrador
public class AppointmentController : AppController
{
    private readonly IAppointmentService _service;    
    public AppointmentController(IAppointmentService service) => _service = service;
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Booking)]   // RL-03: 5/min por paciente autenticado
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Book([FromBody] AppointmentModel.Request request)
    {
        // Pasamos el request y los claims al servicio
        var response = await _service.Book(request, User);
        
        return Created($"/api/appointments/{response.Id}", response);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        await _service.Cancel(id, User);
        // El TFI pide devolver 200 con el texto "ok" al cancelar.
        return Ok("ok");
    }

    // PUT /api/appointments/{id}/status · solo admin: registra la asistencia (ATTENDED | NO_SHOW).
    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = Policies.AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] AppointmentModel.StatusRequest request)
    {
        var updated = await _service.UpdateStatus(id, request.Status);
        return Ok(updated);
    }

}