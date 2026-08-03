using Dsw2026Tpi.CrossCutting.Resources;

namespace Dsw2026Tpi.CrossCutting.Exceptions;

/// <summary>
/// Excepción que se lanza cuando el usuario no tiene permisos suficientes.
/// </summary>
public class AuthorizationException : AppException
{
    public AuthorizationException() : base(nameof(ErrorCodes.AUTHORIZATION_FAILED), ErrorCodes.AUTHORIZATION_FAILED) { }

    // Overload para códigos de autorización específicos del dominio (p. ej. APPOINTMENT_FORBIDDEN)
    // sin perder el 403 que el middleware mapea por tipo. Espeja el patrón de ValidationException.
    public AuthorizationException(string errorCode, string message) : base(errorCode, message) { }
}
