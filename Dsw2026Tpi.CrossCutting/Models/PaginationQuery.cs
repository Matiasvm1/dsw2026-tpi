namespace Dsw2026Tpi.CrossCutting.Models;

/// <summary>
/// Normaliza los parámetros de paginación que llegan por query string.
/// Convención del contrato: pageIndex es base 1, pageSize por defecto 10 y máximo 100.
/// </summary>
public record PaginationQuery
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    public int PageSize { get; }
    public int PageIndex { get; }

    public PaginationQuery(int? pageSize, int? pageIndex)
    {
        PageSize = pageSize is null or <= 0 ? DefaultPageSize : Math.Min(pageSize.Value, MaxPageSize);
        PageIndex = pageIndex is null or <= 0 ? 1 : pageIndex.Value;
    }
}
