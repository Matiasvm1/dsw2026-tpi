using Dsw2026Tpi.CrossCutting.Resources;

namespace Dsw2026Tpi.CrossCutting.Exceptions;

/// <summary>
/// Excepción que se lanza cuando los datos de entrada no son válidos.
/// Puede incluir múltiples errores de validación.
/// </summary>
public class ValidationException : AppException
{
    public ValidationException(string errorCode, string message) : base(errorCode, message) { }

    public ValidationException() : base(nameof(ErrorCodes.VALIDATION_ERROR), ErrorCodes.VALIDATION_ERROR) { }
}
