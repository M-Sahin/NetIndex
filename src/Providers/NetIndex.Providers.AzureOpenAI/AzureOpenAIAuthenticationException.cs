using NetIndex.Core.Abstractions;

namespace NetIndex.Providers.AzureOpenAI;

/// <summary>
/// Thrown when Azure OpenAI authentication or authorization fails.
/// </summary>
public sealed class AzureOpenAIAuthenticationException : NetIndexProviderException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIAuthenticationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="inner">The inner exception that caused the failure.</param>
    /// <param name="errorCode">The provider error code.</param>
    /// <param name="httpStatusCode">The HTTP status code, if applicable.</param>
    public AzureOpenAIAuthenticationException(
        string message,
        Exception? inner = null,
        string? errorCode = null,
        int? httpStatusCode = null)
        : base(
            message,
            isRetryable: false,
            providerName: "AzureOpenAI",
            errorCode: errorCode ?? "auth_failed",
            httpStatusCode: httpStatusCode,
            innerException: inner)
    {
    }
}
