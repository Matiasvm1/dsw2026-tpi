using Dsw2026Tpi.CrossCutting.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Dsw2026Tpi.Application.Services;

public class JwtService
{
    private readonly IConfiguration _config;
    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    // patientId y dni son opcionales porque el token de administrador no los lleva. El módulo de
    // Citas (Fase 2) los necesita para saber qué paciente está haciendo el pedido sin volver a la
    // base en cada request.
    public string GenerateToken(string username, string? role, Guid? patientId = null, string? dni = null)
    {
        if (_config == null) throw new ArgumentNullException();
        var jwtConfig = _config.GetSection("Jwt");
        var keyText = jwtConfig["Key"] ?? throw new ArgumentNullException("Jwt Key");
        var issuer = jwtConfig["Issuer"] ?? throw new ArgumentNullException("Jwt Issuer");
        var audience = jwtConfig["Audience"] ?? throw new ArgumentNullException("Jwt Audience");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresIn = int.Parse(jwtConfig["ExpiresInMinutes"] ?? "60");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, username),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role ?? string.Empty)
        };

        if (patientId.HasValue) claims.Add(new Claim(CustomClaims.PatientId, patientId.Value.ToString()));
        if (!string.IsNullOrWhiteSpace(dni)) claims.Add(new Claim(CustomClaims.Dni, dni));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiresIn),
            signingCredentials: creds
            );

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return tokenString;
    }
}
