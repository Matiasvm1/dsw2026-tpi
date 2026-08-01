using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using System;
using System.Threading.Tasks;

namespace Dsw2026Tpi.Application.Services;

public class SpecialityService : ISpecialityService
{
    private readonly IPersistence _persistence;

    public SpecialityService(IPersistence persistence) => _persistence = persistence;

    public async Task<Pagination<SpecialityModel.Response>> GetAll(PaginationQuery pagination, string? name = null)
    {
        // Recordar que el filtro por nombre debe ser parcial e insensible a mayusculas
        // Al usar .Contains(name) sin StringComparison, EF Core lo termina traduciendo a SQL y la base de datos lo hace case-insensitive por defecto
        // Si name viene vacío, IsNullOrWhiteSpace da true y el filtro deja pasar a todas las especialidades 
        var specialities = await _persistence.Paginate<Speciality, string>(
            pagination.PageSize,
            pagination.PageIndex,
            s => string.IsNullOrWhiteSpace(name) || s.Name.Contains(name),
            s => s.Name);

        // Usamos el método .Map() que ya viene con la clase Pagination.
        // Esto es importantisimo porque mantiene el Total, PageSize y PageIndex originales. Si lo armáramos a mano, podríamos perder esos datos
        return specialities.Map(Map);
    }

    public async Task<SpecialityModel.Response> Create(SpecialityModel.Request request)
    {
        Validate(request);

        // Recordar que el nombre debe ser único. Buscamos si ya existe uno igual en la BD
        var duplicated = await _persistence.First<Speciality>(s => s.Name == request.Name);
        if (duplicated is not null)
        {
            // Usamos el orden obligatorio corregido en la primera parte (Hallazgo H18), primero nameof (código JSON), luego el recurso (mensaje en español)
            throw new ConflictException(nameof(ErrorCodes.SPECIALTY_DUPLICATED), ErrorCodes.SPECIALTY_DUPLICATED);
        }

        // Aplicamos Trim() antes de guardar para limpiar espacios en blanco accidentales que haya tipeado el usuario
        var speciality = new Speciality(request.Name.Trim(), request.Description.Trim());
        await _persistence.Add(speciality);

        return Map(speciality);
    }

    public async Task<SpecialityModel.Response> Update(Guid id, SpecialityModel.Request request)
    {
        Validate(request);

        //  Verificamos que la especialidad que quieren editar realmente exista, sino 404
        var speciality = await _persistence.GetById<Speciality>(id)
            ?? throw new EntityNotFoundException(nameof(Speciality));

        //  Validamos unicidad, pero excluyendo la especialidad actual (s.Id != id) 
        // para que no salte error de "duplicado" si el usuario guarda los cambios sin haber modificado el nombre
        var duplicated = await _persistence.First<Speciality>(s => s.Name == request.Name && s.Id != id);
        if (duplicated is not null)
        {
            throw new ConflictException(nameof(ErrorCodes.SPECIALTY_DUPLICATED), ErrorCodes.SPECIALTY_DUPLICATED);
        }

        //  Usamos el método de dominio (que pasamos a private set) para proteger la entidad
        speciality.Update(request.Name.Trim(), request.Description.Trim());
        await _persistence.Update(speciality);

        return Map(speciality);
    }

    public async Task Delete(Guid id)
    {
        var speciality = await _persistence.GetById<Speciality>(id)
            ?? throw new EntityNotFoundException(nameof(Speciality));

        
        // esto hace una "baja lógica" automáticamente (Deleted = true) en lugar de borrar el registro físicamente de la base
        await _persistence.Delete(speciality);
    }

    private static void Validate(SpecialityModel.Request request)
    {
        // Validamos la longitud del nombre (entre 3 y 100 caracteres)
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 3 or > 100)
        {
            // Todo error de validación debe llevar un .WithDetail() 
            // para decirle al frontend exactamente qué campo falló y por qué (el issue)
            throw new ValidationException(nameof(ErrorCodes.SPECIALTY_NAME_LENGTH), ErrorCodes.SPECIALTY_NAME_LENGTH)
                .WithDetail(nameof(request.Name), "length_between_3_and_100");
        }

        // Validamos la longitud de la descripción (entre 10 y 100 caracteres).
        if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Trim().Length is < 10 or > 100)
        {
            throw new ValidationException(nameof(ErrorCodes.SPECIALTY_DESCRIPTION_LENGTH), ErrorCodes.SPECIALTY_DESCRIPTION_LENGTH)
                .WithDetail(nameof(request.Description), "length_between_10_and_100");
        }
    }

    // Método para no repetir la conversión de Entidad a DTO en cada función
    private static SpecialityModel.Response Map(Speciality s) => new(s.Id, s.Name, s.Description);
}