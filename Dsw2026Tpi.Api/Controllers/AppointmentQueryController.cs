using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.CrossCutting.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Controllers;

// Misma ruta base que el AppointmentController de comandos (par A): no colisionan porque las
// acciones difieren en verbo/sub-ruta. D21: consultas y comandos en controllers separados.
[Route("api/appointments")]
[Authorize]
public class AppointmentQueryController : AppController
{
    private readonly IAppointmentQueryService _service;

    public AppointmentQueryController(IAppointmentQueryService service)
    {
        _service = service;
    }

    // GET /api/appointments/patient?dni=
    // Hereda el [Authorize] de la clase: el paciente SÍ entra (trap §0.1). El 403 por consultar el
    // DNI de otro lo decide el service comparando contra el claim, no una policy de admin.
    [HttpGet("patient")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByPatient([FromQuery] string dni)
    {
        var appointments = await _service.GetByPatient(dni, User);
        return Ok(appointments);
    }

    // GET /api/appointments?date=YYYY-MM-DD  ·  admin-only.
    [HttpGet]
    [Authorize(Policy = Policies.AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByDate(
        [FromQuery] string? date,
        [FromQuery] int? pageSize,
        [FromQuery] int? pageIndex)
    {
        var result = await _service.GetByDate(date, new PaginationQuery(pageSize, pageIndex));
        return Ok(result);
    }

    // GET /api/appointments/search?specialtyId=&doctorId=&dni=&date=  ·  admin-only.
    [HttpGet("search")]
    [Authorize(Policy = Policies.AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] Guid? specialtyId,
        [FromQuery] Guid? doctorId,
        [FromQuery] string? dni,
        [FromQuery] string? date,
        [FromQuery] int? pageSize,
        [FromQuery] int? pageIndex)
    {
        var result = await _service.Search(specialtyId, doctorId, dni, date, new PaginationQuery(pageSize, pageIndex));
        return Ok(result);
    }
}
