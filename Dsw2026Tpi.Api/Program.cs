using Dsw2026Tpi.Api.Configurations;
using Dsw2026Tpi.Api.Middlewares;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Serilog;

namespace Dsw2026Tpi.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Inicializar con un logger simple antes de construir el host
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Iniciando aplicación Dsw2026Tpi.Api");

            var builder = WebApplication.CreateBuilder(args);

            //Configuraciones personalizadas
            builder.AddSerilogConfiguration();
            builder.Services.AddAppIdentity();
            builder.Services.AddAppAuthentication(builder.Configuration);
            builder.Services.AddSwaggerConfiguration();
            builder.Services.AddApplicationPersistence(builder.Configuration);
            builder.Services.AddAppCors(builder.Configuration);
            builder.Services.AddAppDependencies();
            builder.Services.AddAppControllers();
            builder.Services.AddAppRateLimiting(builder.Configuration);
            builder.Services.AddHealthChecks();

            var app = builder.Build();

            app.UseSerilogRequestLogging(options =>
            {
                options.MessageTemplate =
                    "{RequestMethod} {RequestPathWithoutQuery} respondió {StatusCode} en {Elapsed:0.0000} ms";

                options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
                {
                    // Request.Path NO incluye el query string; Request.QueryString sí.
                    diagnosticContext.Set("RequestPathWithoutQuery", httpContext.Request.Path.Value);
                };
            });

            // Va despues del request logging (para que Serilog registre el status final y no un 500
            // con stack trace) y antes de la autenticacion, de modo que toda excepcion aguas abajo
            // salga con el sobre de error del contrato.
            app.UseMiddleware<ExceptionHandlingMiddleware>();

            if (app.Environment.IsProduction())
            {
                app.UseHttpsRedirection();
            }
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // Antes de UseAuthentication: el preflight OPTIONS viaja sin credenciales, y si lo
            // atiende primero la autenticacion se rebota con 401 y el navegador nunca manda la
            // request real.
            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();

            // Despues de UseAuthentication: la politica Booking particiona por el claim patientId,
            // que recien esta disponible en User una vez que corrio la autenticacion.
            app.UseRateLimiter();

            app.MapControllers();
            app.MapHealthChecks("/health-check");

            Log.Information("Aplicación iniciada correctamente");

            await app.SeedAdminUserAsync();

            await app.RunAsync();
        }
        catch (HostAbortedException)
        {
            Log.Information("El host fue abortado (normal durante migraciones de EF Core)");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "La aplicación falló al iniciar");
            throw;
        }
        finally
        {
            Log.Information("Cerrando aplicación");
            await Log.CloseAndFlushAsync();
        }
    }
}

