using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Controllers;

/// <summary>
/// Clase base para configuraciones generales de controladores
/// </summary>
/// <remarks>
/// No declara [Route]: cuando el controlador derivado declara el suyo, ASP.NET Core descarta
/// el de la clase base. Cada controlador declara la ruta completa, con el prefijo "api/".
/// </remarks>
[ApiController]
public abstract class AppController : ControllerBase
{
}

