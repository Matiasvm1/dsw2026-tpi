using Dsw2026Tpi.CrossCutting.Resources;

namespace Dsw2026Tpi.CrossCutting.Exceptions;

/// <summary>
/// Excepción que se lanza cuando el usuario no tiene permisos suficientes.
/// </summary>
public class AuthorizationException : AppException
{
    public AuthorizationException() : base(nameof(ErrorCodes.AUTHORIZATION_FAILED), ErrorCodes.AUTHORIZATION_FAILED) { }
}
