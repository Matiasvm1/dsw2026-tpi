using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Identity;

namespace Dsw2026Tpi.Api.Controllers;

[Route("api/availabilities")]
[Authorize(Policy = Policies.AdminPolicy)] 
public class AvailabilityController : AppController
{
    private readonly IAvailabilityService _service;

    public AvailabilityController(IAvailabilityService service) => _service = service;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] AvailabilityModel.Request request)
    {
        var response = await _service.Create(request);
        
        return Created($"/api/doctors/{request.DoctorId}/availabilities", response);
    }

    [HttpPut] 
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update([FromBody] AvailabilityModel.Request request)
    {
        var response = await _service.Update(request);
        return Ok(response);
    }
}