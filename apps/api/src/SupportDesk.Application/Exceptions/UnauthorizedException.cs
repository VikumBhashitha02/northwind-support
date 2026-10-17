namespace SupportDesk.Application.Exceptions;

/// <summary>
/// Thrown when authentication fails or credentials are rejected. Surfaces as HTTP 401 Unauthorized.
/// </summary>
public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message = "Invalid email or password.") : base(message)
    {
    }
}
