using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data.Extensions;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Identity;

namespace Dsw2026Tpi.Api.Configurations;

public static class SeedConfigurationExtensions
{
    public static async Task SeedAdminUserAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // Los roles salen de Sources/roles.json, que es la unica fuente: asi los identificadores
        // son los mismos en todas las maquinas del equipo. Seedwork es idempotente (no hace nada si
        // ya hay roles cargados).
        // Tiene que correr ACA y no alcanza con el UseSeeding del DbContext: ese delegado solo se
        // dispara desde Migrate/EnsureCreated, que nadie llama al arrancar la API. Sin esto, el
        // AddToRoleAsync de mas abajo no encuentra el rol y el admin queda sin permisos.
        var authenticationDb = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        authenticationDb.Seedwork<IdentityRole>("Sources/roles.json");

        var email = configuration["SeedAdmin:Email"]
            ?? throw new InvalidOperationException("Falta la configuración SeedAdmin:Email");
        var password = configuration["SeedAdmin:Password"]
            ?? throw new InvalidOperationException("Falta la configuración SeedAdmin:Password");

        if (await userManager.FindByEmailAsync(email) is not null) return;

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"No se pudo crear el usuario administrador: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(admin, Roles.Administrator);
    }
}
