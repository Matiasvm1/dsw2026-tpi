using Dsw2026Tpi.CrossCutting.Resources;

namespace Dsw2026Tpi.CrossCutting.Exceptions;

/// <summary>
/// Excepción que se lanza cuando falla la autenticación del usuario.
/// </summary>
public class AuthenticationException : AppException
{
    public AuthenticationException() : base(nameof(ErrorCodes.AUTHENTICATION_FAILED), ErrorCodes.AUTHENTICATION_FAILED) { }
}
