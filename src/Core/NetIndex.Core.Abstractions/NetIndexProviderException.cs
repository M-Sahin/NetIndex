using System;

namespace NetIndex.Core.Abstractions;

/// <summary>
/// Thrown when an external provider (LLM, embedding, etc.) fails.
/// </summary>
/// <remarks>
/// This exception wraps upstream provider errors. No upstream SDK exceptions
/// (e.g., <c>HttpRequestException</c>, Azure SDK exceptions) may surface to consumers (NFR9).
/// 
/// Use <see cref="IsRetryable"/> to determine whether the operation should be retried:
/// <list type="bullet">
///   <item><term><c>true</c></term><description>Rate-limit (HTTP 429), transient HTTP failures (5xx), timeouts.</description></item>
///   <item><term><c>false</c></term><description>Auth failures (401, 403), permanent errors (400, 404).</description></item>
/// </list>
/// </remarks>
public class NetIndexProviderException : NetIndexException
{
    /// <summary>
    /// Gets a value indicating whether this error is retryable.
    /// </summary>
    /// <remarks>
    /// True for rate-limit and transient HTTP failures. False for auth and permanent failures.
    /// </remarks>
    public bool IsRetryable { get; }

    /// <summary>
    /// Gets the name of the provider that failed.
    /// </summary>
    /// <remarks>
    /// Example: "Ollama", "OpenAI", "AzureOpenAI".
    /// </remarks>
    public string? ProviderName { get; }

    /// <summary>
    /// Gets the error code from the provider, if available.
    /// </summary>
    /// <remarks>
    /// Example: "rate_limit_exceeded", "invalid_api_key", "context_length_exceeded".
    /// </remarks>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets the HTTP status code from the provider response, if applicable.
    /// </summary>
    /// <remarks>
    /// Null if the error is not HTTP-based (e.g., connection refused, timeout).
    /// </remarks>
    public int? HttpStatusCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexProviderException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public NetIndexProviderException(string? message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetIndexProviderException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public NetIndexProviderException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance with structured provider error data.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="isRetryable">Whether the operation can be retried.</param>
    /// <param name="providerName">The name of the provider.</param>
    /// <param name="errorCode">The provider error code.</param>
    /// <param name="httpStatusCode">The HTTP status code, if applicable.</param>
    public NetIndexProviderException(
        string? message,
        bool isRetryable,
        string? providerName,
        string? errorCode,
        int? httpStatusCode)
        : base(message)
    {
        IsRetryable = isRetryable;
        ProviderName = providerName;
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
    }
}
