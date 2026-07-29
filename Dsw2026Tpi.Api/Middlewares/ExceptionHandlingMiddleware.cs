using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.CrossCutting.Resources;
using System.Net;
using System.Text.Json;

namespace Dsw2026Tpi.Api.Middlewares;

public class ExceptionHandlingMiddleware
{
    // El contrato exige el sobre de error en camelCase (errorCode, message, details, field, issue).
    // JsonSerializer.Serialize sin opciones usa los nombres de las propiedades tal cual (PascalCase),
    // a diferencia de MVC, que ya serializa en camelCase por defecto.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Se produjo un error durante el procesamiento de la solicitud");
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        ErrorResponse error = ex is AppException exApp ? 
            exApp.Error : 
            new ErrorResponse(nameof(ErrorCodes.UNHANDLED_ERROR), ErrorCodes.UNHANDLED_ERROR);
        var status = ex switch
        {
            ValidationException => HttpStatusCode.BadRequest,
            AuthenticationException => HttpStatusCode.Unauthorized,   // anterior 409 — H03
            AuthorizationException => HttpStatusCode.Forbidden,       // anterior 401 — H04
            EntityNotFoundException => HttpStatusCode.NotFound,
            ConflictException or BusinessRuleException => HttpStatusCode.Conflict,
            _ => HttpStatusCode.InternalServerError,
        };
        var result = JsonSerializer.Serialize(error, SerializerOptions);
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)status;
        await context.Response.WriteAsync(result);
    }
}
