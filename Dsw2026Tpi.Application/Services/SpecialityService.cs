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
        // El filtro es opcional, pero si viene tiene que respetar la misma longitud que el alta.
        if (name is not null && (string.IsNullOrWhiteSpace(name) || name.Trim().Length is < 3 or > 100))
        {
            throw new ValidationException(nameof(ErrorCodes.SPECIALTY_NAME_LENGTH), ErrorCodes.SPECIALTY_NAME_LENGTH)
                .WithDetail(nameof(name), "length_between_3_and_100");
        }

        var normalizedName = name?.Trim();

        // Recordar que el filtro por nombre debe ser parcial e insensible a mayusculas
        // Al usar .Contains(name) sin StringComparison, EF Core lo termina traduciendo a SQL y la base de datos lo hace case-insensitive por defecto
        // Si no vino name, el predicado da true siempre y pasan todas las especialidades
        var specialities = await _persistence.Paginate<Speciality, string>(
            pagination.PageSize,
            pagination.PageIndex,
            s => normalizedName == null || s.Name.Contains(normalizedName),
            s => s.Name);

        // Usamos el método .Map() que ya viene con la clase Pagination.
        // Esto es importantisimo porque mantiene el Total, PageSize y PageIndex originales. Si lo armáramos a mano, podríamos perder esos datos
        return specialities.Map(Map);
    }

    public async Task<SpecialityModel.Response> Create(SpecialityModel.Request request)
    {
        Validate(request);

        // Aplicamos Trim() antes de comparar y de guardar para limpiar espacios en blanco accidentales que haya tipeado el usuario
        var name = request.Name.Trim();
        var description = request.Description.Trim();

        // Recordar que el nombre debe ser único. Buscamos si ya existe uno igual en la BD.
        // Se compara contra el nombre YA normalizado: buscando el crudo, " Cardiología" no
        // encontraría a "Cardiología", se saltearía este 409 y reventaría contra el índice
        // único de la base con un 500.
        var duplicated = await _persistence.First<Speciality>(s => s.Name == name);
        if (duplicated is not null)
        {
            // Usamos el orden obligatorio corregido en la primera parte (Hallazgo H18), primero nameof (código JSON), luego el recurso (mensaje en español)
            throw new ConflictException(nameof(ErrorCodes.SPECIALTY_DUPLICATED), ErrorCodes.SPECIALTY_DUPLICATED);
        }

        var speciality = new Speciality(name, description);
        await _persistence.Add(speciality);

        return Map(speciality);
    }

    public async Task<SpecialityModel.Response> Update(Guid id, SpecialityModel.Request request)
    {
        Validate(request);

        var name = request.Name.Trim();
        var description = request.Description.Trim();

        //  Verificamos que la especialidad que quieren editar realmente exista, sino 404
        var speciality = await _persistence.GetById<Speciality>(id)
            ?? throw new EntityNotFoundException(nameof(ErrorCodes.SPECIALTY_NOT_FOUND), ErrorCodes.SPECIALTY_NOT_FOUND);

        //  Validamos unicidad sobre el nombre normalizado, pero excluyendo la especialidad actual (s.Id != id)
        // para que no salte error de "duplicado" si el usuario guarda los cambios sin haber modificado el nombre
        var duplicated = await _persistence.First<Speciality>(s => s.Name == name && s.Id != id);
        if (duplicated is not null)
        {
            throw new ConflictException(nameof(ErrorCodes.SPECIALTY_DUPLICATED), ErrorCodes.SPECIALTY_DUPLICATED);
        }

        //  Usamos el método de dominio (que pasamos a private set) para proteger la entidad
        speciality.Update(name, description);
        await _persistence.Update(speciality);

        return Map(speciality);
    }

    public async Task Delete(Guid id)
    {
        var speciality = await _persistence.GetById<Speciality>(id)
            ?? throw new EntityNotFoundException(nameof(ErrorCodes.SPECIALTY_NOT_FOUND), ErrorCodes.SPECIALTY_NOT_FOUND);

        
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