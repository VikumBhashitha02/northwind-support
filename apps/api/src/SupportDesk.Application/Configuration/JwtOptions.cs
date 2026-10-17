namespace SupportDesk.Application.Configuration;

/// <summary>
/// Configuration options for signing and validating JWT authentication tokens.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// Cryptographic signing key (minimum 32 characters for HMAC-SHA256).
    /// Must be provided via environment variable in production.
    /// </summary>
    public string SigningKey { get; set; } = "LocalDevelopmentSecretKeyShouldBeAtLeast32BytesLong!";

    public string Issuer { get; set; } = "NorthwindSupportDesk";

    public string Audience { get; set; } = "NorthwindSupportDesk";

    /// <summary>Token lifetime in hours. Defaults to an 8-hour work shift.</summary>
    public int ExpiryHours { get; set; } = 8;
}
