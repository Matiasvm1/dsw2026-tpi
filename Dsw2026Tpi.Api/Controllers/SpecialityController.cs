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
// REGLA GENERAL: hay que estar logueado para entrar a cualquier acción de este archivo.
// El permiso de Administrador va acción por acción, NO acá: los atributos [Authorize] de clase y
// de acción se SUMAN, no se pisan. Con AdminPolicy a nivel de clase, un [Authorize] suelto en el
// GET no lo libera: el paciente igual tiene que cumplir las dos y se come un 403.
[Authorize]
public class SpecialityController : AppController // Heredamos de AppController por el "Bloque de Arranque"
{
    private readonly ISpecialityService _service;

    public SpecialityController(ISpecialityService service) => _service = service;

    [HttpGet]
    // El paciente NECESITA listar especialidades para poder sacar un turno (D33), así que esta
    // acción se queda solamente con el [Authorize] de la clase.
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int? pageSize, [FromQuery] int? pageIndex, [FromQuery] string? name = null)
    {
        // el controller no tiene NADA de lógica ni try/catch. Solo recibe el pedido y se lo pasa a tu Service
        var result = await _service.GetAll(new PaginationQuery(pageSize, pageIndex), name);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminPolicy)]
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
    [Authorize(Policy = Policies.AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] SpecialityModel.Request request)
    {
        return Ok(await _service.Update(id, request));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.Delete(id);
        // El TFI pide devolver 200 con el texto "ok" en la baja logica.
        return Ok("ok");
    }
}