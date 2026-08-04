using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Helpers;
using Dsw2026Tpi.CrossCutting.Identity;
using Dsw2026Tpi.CrossCutting.Resources;
using Dsw2026Tpi.Data.Identity;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Dsw2026Tpi.Application.Services;

public class AuthenticationService : IAuthenticationService
{
    private const int DniMinLength = 7;
    private const int DniMaxLength = 8; // Contrato: el login de paciente admite DNI de 7 u 8 dígitos.

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ISignInService _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IPersistence _persistence;
    private readonly JwtService _jwtService;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(UserManager<ApplicationUser> userManager,
        ISignInService signInManager,
        RoleManager<IdentityRole> roleManager,
        IPersistence persistence,
        JwtService jwtService,
        ILogger<AuthenticationService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _persistence = persistence;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<LoginAdminModel.Response> LoginAdmin(LoginAdminModel.Request request)
    {
        if (!request.Email.IsEmailValid()) throw new AuthenticationException();

        // Contrato del login de admin: password obligatorio y de al menos 8 caracteres. Es una
        // validación de entrada (400), independiente de si la credencial es correcta o no.
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8)
            throw new ValidationException(
                    nameof(ErrorCodes.ADMIN_PASSWORD_LENGTH),
                    ErrorCodes.ADMIN_PASSWORD_LENGTH)
                .WithDetail("password", "min_length_8");

        var user = await _userManager.FindByEmailAsync(request.Email) ?? throw new AuthenticationException();
        var result = await _signInManager.CheckPassword(user, request.Password);

        if (!result)
        {
            _logger.LogError("Intento de login fallido para: {Email}", request.Email);
            throw new AuthenticationException();
        }

        var role = (await _userManager.GetRolesAsync(user)).FirstOrDefault();

        var token  = _jwtService.GenerateToken(user.UserName!, role);

        _logger.LogInformation("Login de administrador exitoso para: {Email}", request.Email);

        // El claim lleva el nombre interno del rol; la respuesta, el que exige el contrato.
        return new LoginAdminModel.Response(
            token,
            Roles.ToResponse(role)
        );
    }

    public async Task<LoginPatientModel.Response> LoginPatient(LoginPatientModel.Request request)
    {
        if (!request.Email.IsEmailValid())
        {
            throw new ValidationException().WithDetail("email", "invalid_format");
        }

        // El contrato recibe el DNI como número y Patient.Dni se guarda como string (D12): la
        // conversión se hace acá, una sola vez, y de ahí en adelante se compara siempre string.
        var dni = request.Dni.ToString();

        if (request.Dni <= 0 || dni.Length is < DniMinLength or > DniMaxLength)
        {
            throw new ValidationException(
                    nameof(ErrorCodes.PATIENT_DNI_INVALID),
                    ErrorCodes.PATIENT_DNI_INVALID)
                .WithDetail("dni", $"length_between_{DniMinLength}_and_{DniMaxLength}");
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        Patient patient;

        if (user is null)
        {
            (user, patient) = await SelfRegister(request.Email, dni);
        }
        else
        {
            patient = await AuthenticatePatient(user, dni);
        }

        var token = _jwtService.GenerateToken(user.UserName!, Roles.Patient, patient.Id, patient.Dni);

        return new LoginPatientModel.Response(token, Roles.ToResponse(Roles.Patient));
    }

    /// <summary>
    /// Autorregistro del paciente (RN06): el primer login con un email desconocido crea el usuario
    /// y el paciente.
    /// </summary>
    private async Task<(ApplicationUser User, Patient Patient)> SelfRegister(string email, string dni)
    {
        // El DNI identifica a una persona y tiene índice único en la base: si ya está tomado por
        // otro email, esto no es un alta nueva sino un par de credenciales que no corresponde.
        // Sin este chequeo el INSERT viola el índice y la API contesta 500.
        // Se responde 401 y no 409: decir "ese DNI ya está registrado" se lo confirmaría a
        // cualquiera que esté probando documentos, que es el mismo motivo por el que el DNI
        // equivocado tampoco devuelve 404.
        if (await _persistence.First<Patient>(p => p.Dni == dni) is not null)
        {
            _logger.LogError("Intento de autorregistro con un DNI ya registrado: {Email}", email);
            throw new AuthenticationException();
        }

        // El paciente se autentica con email + DNI, no tiene contraseña: CreateAsync sin el
        // segundo parámetro.
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user);

        if (!result.Succeeded) throw new ConflictException(nameof(ErrorCodes.REGISTER_USER_CONFLICT),
            ErrorCodes.REGISTER_USER_CONFLICT)
                .WithDetail(result.Errors.Select(e => (e.Code, e.Description)));

        try
        {
            var roleResult = await _userManager.AddToRoleAsync(user, Roles.Patient);

            if (!roleResult.Succeeded) throw new ConflictException(nameof(ErrorCodes.REGISTER_USER_CONFLICT),
                ErrorCodes.REGISTER_USER_CONFLICT)
                    .WithDetail(roleResult.Errors.Select(e => (e.Code, e.Description)));

            // FullName queda en NULL (D35): el login no manda ningún nombre y no hay que
            // inventarle uno al paciente.
            var patient = await _persistence.Add(new Patient(user.Id, dni));

            _logger.LogInformation("Paciente autorregistrado: {Email}", email);

            return (user, patient);
        }
        catch
        {
            // ApplicationUser y Patient viven en DbContexts distintos, así que no hay una
            // transacción que cubra a los dos. Si falla la segunda mitad se compensa a mano; sin
            // esto queda un usuario huérfano que nunca más puede loguearse.
            await _userManager.DeleteAsync(user);
            throw;
        }
    }

    /// <summary>
    /// Login de un paciente ya registrado: el DNI hace de credencial.
    /// </summary>
    private async Task<Patient> AuthenticatePatient(ApplicationUser user, string dni)
    {
        var patient = await _persistence.First<Patient>(p => p.UserId == user.Id);

        // Sin paciente asociado es un administrador entrando por la puerta del paciente.
        // Un DNI que no coincide responde 401 y no 404: con 404 le confirmaríamos a un atacante
        // que ese email existe en el sistema.
        if (patient is null || patient.Dni != dni)
        {
            _logger.LogError("Intento de login de paciente fallido para: {Email}", user.Email);
            throw new AuthenticationException();
        }

        _logger.LogInformation("Login de paciente exitoso para: {Email}", user.Email);

        return patient;
    }

    public async Task<RegisterModel.Response> Register(RegisterModel.Request request)
    {
        if (!request.Email.IsEmailValid()) throw new ValidationException(
            nameof(ErrorCodes.REGISTER_USER_INVALID), ErrorCodes.REGISTER_USER_INVALID);

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded) throw new ConflictException(nameof(ErrorCodes.REGISTER_USER_CONFLICT),
            ErrorCodes.REGISTER_USER_CONFLICT)
                .WithDetail(result.Errors.Select(e => (e.Code, e.Description)));
       
        _ = await _userManager.AddToRoleAsync(user, Roles.Administrator);

        _logger.LogInformation("Usuario registrado: {Email}", request.Email);

        return new RegisterModel.Response(request.Email);
    }
}
