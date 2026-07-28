using Dsw2026Tpi.CrossCutting.Models;

namespace Dsw2026Tpi.CrossCutting.Exceptions;

/// <summary>
/// Excepción base para todas las excepciones de la aplicación.
/// Incluye código de error, status HTTP y datos adicionales para facilitar el manejo global.
/// </summary>
public abstract class AppException : Exception
{
    /// <summary>
    /// Código de error único para identificar el tipo de error
    /// </summary>
    public ErrorResponse Error { get; }


    /// <summary>
    /// Datos adicionales sobre el error (para debugging o información extra)
    /// </summary>
    public Dictionary<string, object>? AdditionalData { get; set; }

    protected AppException(
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = new ErrorResponse(errorCode, message);
    }

    /// <summary>
    /// Agrega datos adicionales al error
    /// </summary>
    public AppException WithDetail(string field, string issue)
    {
        Error.AddDetail(field, issue);
        return this;
    }
    public AppException WithDetail(IEnumerable<(string, string)> details)
    {
        Error.AddDetail(details);
        return this;
    }
}
