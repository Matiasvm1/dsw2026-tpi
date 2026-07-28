using Dsw2026Tpi.CrossCutting.Resources;

namespace Dsw2026Tpi.CrossCutting.Exceptions;

/// <summary>
/// Excepción que se lanza cuando una entidad no se encuentra en la base de datos.
/// </summary>
public class EntityNotFoundException : AppException
{
    public EntityNotFoundException(string entityName)
    : base(nameof(ErrorCodes.ENTITY_NOTFOUND), string.Format(ErrorCodes.ENTITY_NOTFOUND, entityName)) { }
}
