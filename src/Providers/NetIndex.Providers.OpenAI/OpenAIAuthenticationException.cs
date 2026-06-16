using NetIndex.Core.Abstractions;

namespace NetIndex.Providers.OpenAI;

/// <summary>
/// Thrown when OpenAI authentication or authorization fails.
/// </summary>
public sealed class OpenAIAuthenticationException : NetIndexProviderException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIAuthenticationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception that caused the failure.</param>
    /// <param name="errorCode">The provider error code.</param>
    /// <param name="httpStatusCode">The HTTP status code, if applicable.</param>
    public OpenAIAuthenticationException(
        string message,
        Exception? inner = null,
        string? errorCode = null,
        int? httpStatusCode = null)
        : base(
            message,
            isRetryable: false,
            providerName: "OpenAI",
            errorCode: errorCode ?? "auth_failed",
            httpStatusCode: httpStatusCode,
            innerException: inner)
    {
    }
}
