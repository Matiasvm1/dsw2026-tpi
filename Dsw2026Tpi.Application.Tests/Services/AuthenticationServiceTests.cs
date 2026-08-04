using System.Linq.Expressions;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.Application.Services;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Data.Identity;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace Dsw2026Tpi.Application.Tests.Services;

/// <summary>
/// Tests del login. Identity (UserManager/RoleManager) se mockea a través de sus stores; el
/// JwtService es real con una config en memoria. Foco: las reglas de negocio y de seguridad —
/// email/DNI inválidos, credenciales incorrectas y el autorregistro (RN06). Los fallos de auth
/// devuelven siempre AuthenticationException (401) para no filtrar qué email/DNI existe.
/// </summary>
public class AuthenticationServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _userManager;
    private readonly Mock<ISignInService> _signIn = new();
    private readonly Mock<IPersistence> _persistence = new();
    private readonly AuthenticationService _sut;

    public AuthenticationServiceTests()
    {
        _userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        var roleManager = new Mock<RoleManager<IdentityRole>>(
            Mock.Of<IRoleStore<IdentityRole>>(), null!, null!, null!, null!);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["Jwt:ExpiresInMinutes"] = "60",
        }).Build();

        _sut = new AuthenticationService(
            _userManager.Object, _signIn.Object, roleManager.Object,
            _persistence.Object, new JwtService(config),
            Mock.Of<ILogger<AuthenticationService>>());
    }

    private static ApplicationUser User(string email = "admin@system.com") =>
        new() { Id = Guid.NewGuid().ToString(), UserName = email, Email = email };

    private void SetupFirstPatient(Patient? patient) =>
        _persistence.Setup(p => p.First(It.IsAny<Expression<Func<Patient, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(patient);

    // ---------------------------------------------------------------- LoginAdmin

    [Fact]
    public async Task LoginAdmin_EmailInvalido_LanzaAuthenticationException()
    {
        await Assert.ThrowsAsync<AuthenticationException>(
            () => _sut.LoginAdmin(new LoginAdminModel.Request("no-es-email", "x")));
    }

    [Fact]
    public async Task LoginAdmin_UsuarioInexistente_LanzaAuthenticationException()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        await Assert.ThrowsAsync<AuthenticationException>(
            () => _sut.LoginAdmin(new LoginAdminModel.Request("admin@system.com", "wrongpass")));
    }

    [Fact]
    public async Task LoginAdmin_PasswordIncorrecta_LanzaAuthenticationException()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(User());
        _signIn.Setup(s => s.CheckPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<AuthenticationException>(
            () => _sut.LoginAdmin(new LoginAdminModel.Request("admin@system.com", "wrongpass")));
    }

    [Fact]
    public async Task LoginAdmin_CredencialesValidas_DevuelveTokenYRol()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(User());
        _signIn.Setup(s => s.CheckPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(true);
        _userManager.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new List<string> { Roles.Administrator });

        var result = await _sut.LoginAdmin(new LoginAdminModel.Request("admin@system.com", "Admin1234!"));

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(Roles.AdministratorResponse, result.Role); // "ADMINISTRADOR"
    }

    [Fact]
    public async Task LoginAdmin_PasswordCorta_LanzaValidationException()
    {
        // Email válido: llega a la validación de largo de contraseña (mínimo 8) antes del auth.
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.LoginAdmin(new LoginAdminModel.Request("admin@system.com", "corta")));
        Assert.Equal(nameof(ErrorCodes.ADMIN_PASSWORD_LENGTH), ex.Error.ErrorCode);
    }

    // ---------------------------------------------------------------- LoginPatient

    [Fact]
    public async Task LoginPatient_EmailInvalido_LanzaValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _sut.LoginPatient(new LoginPatientModel.Request("no-es-email", 12345678)));
    }

    [Fact]
    public async Task LoginPatient_DniInvalido_Lanza400()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sut.LoginPatient(new LoginPatientModel.Request("nuevo@test.com", 123)));
        Assert.Equal(nameof(ErrorCodes.PATIENT_DNI_INVALID), ex.Error.ErrorCode);
    }

    // Autorregistro (RN06) con un DNI ya tomado por otro email: 401, no 409.
    [Fact]
    public async Task LoginPatient_AutorregistroConDniYaRegistrado_LanzaAuthenticationException()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        SetupFirstPatient(new Patient("otro-user", "12345678")); // el DNI ya existe

        await Assert.ThrowsAsync<AuthenticationException>(
            () => _sut.LoginPatient(new LoginPatientModel.Request("nuevo@test.com", 12345678)));
    }

    // Usuario existente cuyo DNI no coincide: 401 (no 404, para no confirmar que el email existe).
    [Fact]
    public async Task LoginPatient_UsuarioExistenteConDniQueNoCoincide_LanzaAuthenticationException()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(User("pac@test.com"));
        SetupFirstPatient(new Patient("user-id", "99999999")); // dni distinto al del request

        await Assert.ThrowsAsync<AuthenticationException>(
            () => _sut.LoginPatient(new LoginPatientModel.Request("pac@test.com", 12345678)));
    }

    [Fact]
    public async Task LoginPatient_AutorregistroExitoso_DevuelveTokenYRolPaciente()
    {
        _userManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        SetupFirstPatient(null); // DNI libre
        _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _persistence.Setup(p => p.Add(It.IsAny<Patient>())).ReturnsAsync((Patient p) => p);

        var result = await _sut.LoginPatient(new LoginPatientModel.Request("nuevo@test.com", 12345678));

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(Roles.PatientResponse, result.Role); // "PACIENTE"
    }
}
