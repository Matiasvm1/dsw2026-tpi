using System;
using System.Threading.Tasks;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Identity; // Para poder usar Policies.AdminPolicy
using Dsw2026Tpi.CrossCutting.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Controllers;

//  La ruta va ("specialties") aunque la clase sea "Speciality" 
[Route("api/specialties")]
// REGLA GENERAL: Todo este archivo requiere permisos de Administrador por defecto
[Authorize(Policy = Policies.AdminPolicy)]
public class SpecialityController : AppController // Heredamos de AppController por el "Bloque de Arranque" 
{
    private readonly ISpecialityService _service;

    public SpecialityController(ISpecialityService service) => _service = service;

    [HttpGet]
    // El paciente NECESITA listar especialidades para poder sacar un turno.
    // Al poner [Authorize] acá, sobreescribimos la regla de Admin de arriba y dejamos que CUALQUIER usuario logueado use el GET
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int? pageSize, [FromQuery] int? pageIndex, [FromQuery] string? name = null)
    {
        // el controller no tiene NADA de lógica ni try/catch. Solo recibe el pedido y se lo pasa a tu Service
        var result = await _service.GetAll(new PaginationQuery(pageSize, pageIndex), name);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] SpecialityModel.Request request)
    {
        var created = await _service.Create(request);

        // El documento prohíbe usar "CreatedAtAction" porque no tenemos un endpoint de tipo GET por ID.
        // Si lo usamos, daría un error 500 al guardar. Se arma la URI a mano
        return Created($"/api/specialties/{created.Id}", created);
    }

    // La restricción "{id:guid}" frena el request si alguien manda texto en vez de un ID válido y devuelve un 404
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SpecialityModel.Request request)
    {
        return Ok(await _service.Update(id, request));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.Delete(id);
        // Devuelve 204 (No Content) porque el borrado fue exitoso pero no hay datos para devolverle a la pantalla
        return NoContent();
    }
}