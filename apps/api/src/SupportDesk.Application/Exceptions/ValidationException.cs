namespace SupportDesk.Application.Exceptions;

/// <summary>
/// Thrown when a request fails validation. Surfaces as HTTP 400 with a problem document
/// listing the failures per field.
/// </summary>
public sealed class ValidationException : Exception
{
    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationException(string propertyName, string errorMessage)
        : this(new Dictionary<string, string[]> { [propertyName] = [errorMessage] })
    {
    }

    /// <summary>Field name to the messages for that field.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
