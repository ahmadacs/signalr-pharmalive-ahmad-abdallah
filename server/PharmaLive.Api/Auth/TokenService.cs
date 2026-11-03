using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PharmaLive.Api.Contracts;

namespace PharmaLive.Api.Auth;

/// <summary>
/// DEV-ONLY token issuer: anyone can request a token for a name and a role.
/// In a real product the token comes from an identity provider; the hub side is identical.
/// </summary>
public sealed class TokenService(IConfiguration config)
{
    public const string DoctorRole = "Doctor";
    public const string PharmacistRole = "Pharmacist";
    public const string DisplayRole = "Display";

    public static readonly string[] Roles = [DoctorRole, PharmacistRole, DisplayRole];

    public static string? NormalizeRole(string? role) =>
        Roles.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static SymmetricSecurityKey KeyFrom(IConfiguration config)
    {
        var key = config["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be configured with at least 32 bytes (see appsettings.json).");
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }

    public TokenResponse Issue(string userName, string role)
    {
        var expires = DateTimeOffset.UtcNow.AddHours(8);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userName),
                new Claim(JwtRegisteredClaimNames.Name, userName),
                new Claim("role", role),
            ]),
            SigningCredentials = new SigningCredentials(KeyFrom(config), SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new TokenResponse(token, userName, role, expires);
    }
}
