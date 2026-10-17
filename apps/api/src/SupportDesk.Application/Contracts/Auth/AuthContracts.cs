using FluentValidation;

namespace SupportDesk.Application.Contracts.Auth;

/// <summary>
/// Credentials submitted to authenticate an agent.
/// </summary>
public sealed record LoginRequest
{
    public string Email { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}

/// <summary>
/// Authenticated session token and the agent's identity.
/// </summary>
public sealed record LoginResponse
{
    public required string Token { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }

    public required CurrentUserDto User { get; init; }
}

/// <summary>
/// Essential agent identity returned to greet the signed-in user and populate application state.
/// </summary>
public sealed record CurrentUserDto(int Id, string FullName, string Email);
