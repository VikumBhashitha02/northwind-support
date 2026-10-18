using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Configuration;
using SupportDesk.Domain.Aggregates.Agents;

namespace SupportDesk.Infrastructure.Services;

/// <summary>
/// Generates HMAC-SHA256 signed JWT tokens containing agent claims (Id, Name, Email).
/// </summary>
public sealed class JwtTokenGenerator(IOptions<JwtOptions> options, IClock clock) : IJwtTokenGenerator
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(Agent agent)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = clock.UtcNow;
        var expiresAtUtc = now.AddHours(_options.ExpiryHours);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, agent.Id.ToString()),
            new Claim(ClaimTypes.Name, agent.FullName),
            new Claim(ClaimTypes.Email, agent.Email),
            new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, agent.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            IssuedAt = now,
            Expires = expiresAtUtc,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);

        return (handler.WriteToken(token), expiresAtUtc);
    }
}
