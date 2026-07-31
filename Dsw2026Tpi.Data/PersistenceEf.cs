using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Dsw2026Tpi.Data;

public class PersistenceEf: IPersistence
{
    private readonly Dsw2026TpiDbContext _context;

    public PersistenceEf(Dsw2026TpiDbContext context)
    {
        _context = context;
    }

    public async Task<T> Add<T>(T entity) where T : EntityBase
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.AddAsync(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<T> Delete<T>(T entity) where T : EntityBase
    {
        entity.SoftDelete();
        _context.Update(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public async Task<T?> First<T>(Expression<Func<T, bool>> predicate, params string[] include) where T : EntityBase
    {
        return await Include(Query<T>(), include).FirstOrDefaultAsync(predicate);
    }

    public async Task<IEnumerable<T>?> GetAll<T>(params string[] include) where T : EntityBase
    {
        return await Include(Query<T>(), include).ToListAsync();
    }

    public async Task<T?> GetById<T>(Guid id, params string[] include) where T : EntityBase
    {
        return await Include(Query<T>(), include).FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<IEnumerable<T>?> GetFiltered<T>(Expression<Func<T, bool>> predicate, params string[] include) where T : EntityBase
    {
        return await Include(Query<T>(), include).Where(predicate).ToListAsync();
    }

    public async Task<T> Update<T>(T entity) where T : EntityBase
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _context.Update(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    // pageIndex es base 1 y la respuesta devuelve los mismos valores que pidió el cliente.
    // Los parámetros ya llegan normalizados desde PaginationQuery (capa Application/Api).
    // Si la página pedida no existe se devuelve data vacía con el total real: no se retrocede
    // de página, porque el cliente terminaría recibiendo datos de una página que no pidió.
    public async Task<Pagination<T>> Paginate<T, TKey>(int pageSize, int pageIndex, Expression<Func<T, bool>> predicate, Expression<Func<T, TKey>> sortOrder, params string[] includes) where T : EntityBase
    {
        var skip = (pageIndex - 1) * pageSize;

        var filtered = Include(Query<T>(), includes).Where(predicate);
        var total = await filtered.CountAsync();

        var data = await filtered.OrderBy(sortOrder).Skip(skip).Take(pageSize).ToListAsync();

        return new Pagination<T>(pageSize, pageIndex, total, data);
    }

    // Punto de entrada único de las lecturas. La baja lógica se aplica acá, explícitamente sobre
    // la raíz, en vez de dejarla en manos del filtro global del DbContext.
    //
    // El filtro global también se aplicaba a las navegaciones incluidas, y eso rompía las
    // consultas: al incluir una navegación requerida (Doctor -> Speciality) EF genera un
    // INNER JOIN, y si el principal estaba dado de baja la fila del dependiente desaparecía de
    // los resultados. Pero CountAsync() descarta los Include, así que el total la seguía
    // contando: la respuesta se contradecía sola (total: 2 con una sola fila en data).
    //
    // IgnoreQueryFilters() apaga el filtro global de toda la consulta y el Where lo repone solo
    // sobre la raíz. Así el dependiente sobrevive a la baja de su principal, y total y data
    // cuentan siempre las mismas filas.
    //
    // Consecuencia a tener presente: las navegaciones incluidas ya no ocultan los eliminados. En
    // las navegaciones de referencia es justamente lo que queremos (el médico conserva su
    // especialidad histórica). Si alguna vez se incluye una colección y sus eliminados no deben
    // aparecer, ese filtro va explícito en el service.
    private IQueryable<T> Query<T>() where T : EntityBase
    {
        return _context.Set<T>().IgnoreQueryFilters().Where(e => !e.Deleted);
    }

    private static IQueryable<T> Include<T>(IQueryable<T> query, string[] includes) where T : EntityBase
    {
        var includedQuery = query;

        foreach (var include in includes)
        {
            includedQuery = includedQuery.Include(include);
        }
        return includedQuery;
    }
}
