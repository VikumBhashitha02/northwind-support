using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Auth.Commands.Login;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using Xunit;

namespace SupportDesk.UnitTests.Application.Features.Auth;

public sealed class LoginCommandHandlerTests
{
    private readonly Mock<IAgentRepository> _agentRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IJwtTokenGenerator> _tokenGenerator = new();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _handler = new LoginCommandHandler(
            _agentRepository.Object,
            _passwordHasher.Object,
            _tokenGenerator.Object,
            NullLogger<LoginCommandHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownEmail_ThrowsUnauthorizedException()
    {
        _agentRepository
            .Setup(r => r.GetByEmailAsync("unknown@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var request = new LoginRequest { Email = "unknown@example.com", Password = "Password123!" };

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => _handler.HandleAsync(request, CancellationToken.None));
        Assert.Equal("Invalid email or password.", ex.Message);
        _tokenGenerator.Verify(t => t.GenerateToken(It.IsAny<Agent>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithIncorrectPassword_ThrowsUnauthorizedException()
    {
        var agent = new Agent("Alex Morgan", "alex@northwind.test", 5, DateTime.UtcNow);
        agent.SetPasswordHash("valid_hash");

        _agentRepository
            .Setup(r => r.GetByEmailAsync(agent.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        _passwordHasher
            .Setup(h => h.VerifyPassword("WrongPassword!", "valid_hash"))
            .Returns(false);

        var request = new LoginRequest { Email = agent.Email, Password = "WrongPassword!" };

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => _handler.HandleAsync(request, CancellationToken.None));
        Assert.Equal("Invalid email or password.", ex.Message);
        _tokenGenerator.Verify(t => t.GenerateToken(It.IsAny<Agent>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithInactiveAgent_ThrowsUnauthorizedException()
    {
        var agent = new Agent("Yuki Tanaka", "yuki@northwind.test", 5, DateTime.UtcNow);
        agent.Deactivate();
        agent.SetPasswordHash("valid_hash");

        _agentRepository
            .Setup(r => r.GetByEmailAsync(agent.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        _passwordHasher
            .Setup(h => h.VerifyPassword("Password123!", "valid_hash"))
            .Returns(true);

        var request = new LoginRequest { Email = agent.Email, Password = "Password123!" };

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => _handler.HandleAsync(request, CancellationToken.None));
        Assert.Equal("Invalid email or password.", ex.Message);
        _tokenGenerator.Verify(t => t.GenerateToken(It.IsAny<Agent>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithValidCredentials_ReturnsLoginResponse()
    {
        var agent = new Agent("Alex Morgan", "alex@northwind.test", 5, DateTime.UtcNow);
        agent.SetPasswordHash("valid_hash");

        _agentRepository
            .Setup(r => r.GetByEmailAsync(agent.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        _passwordHasher
            .Setup(h => h.VerifyPassword("Password123!", "valid_hash"))
            .Returns(true);

        var expiresAtUtc = DateTime.UtcNow.AddHours(8);
        _tokenGenerator
            .Setup(t => t.GenerateToken(agent))
            .Returns(("signed.jwt.token", expiresAtUtc));

        var request = new LoginRequest { Email = agent.Email, Password = "Password123!" };

        var response = await _handler.HandleAsync(request, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("signed.jwt.token", response.Token);
        Assert.Equal(expiresAtUtc, response.ExpiresAtUtc);
        Assert.Equal(agent.FullName, response.User.FullName);
        Assert.Equal(agent.Email, response.User.Email);
    }
}
