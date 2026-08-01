using Dsw2026Tpi.CrossCutting.Resources;

namespace Dsw2026Tpi.CrossCutting.Exceptions;

/// <summary>
/// Excepción que se lanza cuando una entidad no se encuentra en la base de datos.
/// </summary>
public class EntityNotFoundException : AppException
{
    public EntityNotFoundException(string entityName)
    : base(nameof(ErrorCodes.ENTITY_NOTFOUND), string.Format(ErrorCodes.ENTITY_NOTFOUND, entityName)) { }

    /// <summary>
    /// Variante con el código de error propio del recurso (SPECIALTY_NOT_FOUND, DOCTOR_NOT_FOUND,
    /// APPOINTMENT_NOT_FOUND), que es lo que piden las tablas de endpoints de la Fase 1. El
    /// constructor de un solo parámetro sigue existiendo para las entidades que no tienen código
    /// propio y caen en el ENTITY_NOTFOUND genérico de la Fase 0.
    /// </summary>
    public EntityNotFoundException(string errorCode, string message)
    : base(errorCode, message) { }
}
