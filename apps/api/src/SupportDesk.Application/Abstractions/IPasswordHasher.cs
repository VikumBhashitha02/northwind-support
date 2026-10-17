namespace SupportDesk.Application.Abstractions;

/// <summary>
/// Hashes and verifies agent passwords using a secure, vetted cryptographic algorithm.
/// </summary>
public interface IPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string password, string passwordHash);
}
