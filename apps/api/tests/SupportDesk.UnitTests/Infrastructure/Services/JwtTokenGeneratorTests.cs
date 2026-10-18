using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SupportDesk.Application.Configuration;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Infrastructure.Services;
using SupportDesk.UnitTests.TestDoubles;
using Xunit;

namespace SupportDesk.UnitTests.Infrastructure.Services;

public sealed class JwtTokenGeneratorTests
{
    private const string TestKey = "ThisIsASecretKeyForTestingPurposes1234567890!";
    private const string Issuer = "NorthwindSupportDesk.Test";
    private const string Audience = "NorthwindSupportDesk.TestApp";

    private readonly JwtOptions _options = new()
    {
        SigningKey = TestKey,
        Issuer = Issuer,
        Audience = Audience,
        ExpiryHours = 8
    };

    private readonly FixedClock _clock = new(DateTime.UtcNow);

    [Fact]
    public void GenerateToken_ProducesValidJwt_ThatCanBeValidated()
    {
        var generator = new JwtTokenGenerator(Options.Create(_options), _clock);
        var agent = new Agent("Sara Conner", "sara@northwind.test", 5, _clock.UtcNow);

        var (token, expiresAtUtc) = generator.GenerateToken(agent);

        Assert.NotEmpty(token);
        Assert.Equal(_clock.UtcNow.AddHours(8), expiresAtUtc);

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

        Assert.NotNull(principal);
        Assert.Equal(agent.FullName, principal.FindFirst(ClaimTypes.Name)?.Value);
        Assert.Equal(agent.Email, principal.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public void ValidateToken_WithTamperedToken_FailsValidation()
    {
        var generator = new JwtTokenGenerator(Options.Create(_options), _clock);
        var agent = new Agent("Sara Conner", "sara@northwind.test", 5, _clock.UtcNow);

        var (token, _) = generator.GenerateToken(agent);
        var tamperedToken = token + "corrupt";

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        Assert.ThrowsAny<SecurityTokenException>(() =>
            tokenHandler.ValidateToken(tamperedToken, validationParameters, out _));
    }

    [Fact]
    public void ValidateToken_WithExpiredToken_FailsValidation()
    {
        var generator = new JwtTokenGenerator(Options.Create(_options), _clock);
        var agent = new Agent("Sara Conner", "sara@northwind.test", 5, _clock.UtcNow);

        var (token, _) = generator.GenerateToken(agent);

        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, securityToken, validationParameters) =>
            {
                // Simulate time advancing 10 hours later
                var futureTime = _clock.UtcNow.AddHours(10);
                return expires.HasValue && expires.Value > futureTime;
            }
        };

        Assert.ThrowsAny<SecurityTokenException>(() =>
            tokenHandler.ValidateToken(token, validationParameters, out _));
    }
}
