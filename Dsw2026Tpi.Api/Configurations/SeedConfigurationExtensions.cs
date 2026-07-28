using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.Data.Identity;
using Microsoft.AspNetCore.Identity;

namespace Dsw2026Tpi.Api.Configurations;

public static class SeedConfigurationExtensions
{
    public static async Task SeedAdminUserAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        // Red de contención: si por algún motivo los roles no están, se crean acá.
        // Es idempotente, así que no molesta cuando ya existen.
        foreach (var role in new[] { Roles.Administrator, Roles.Patient })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

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
