using NetIndex.Core.Abstractions;

namespace NetIndex.Providers.Ollama;

/// <summary>Thrown when a connection to the Ollama API cannot be established.</summary>
public sealed class OllamaConnectionException : NetIndexProviderException
{
    /// <summary>Initializes with a message and optional inner exception.</summary>
    /// <param name="message">The error message describing the connection failure.</param>
    /// <param name="innerException">The underlying exception that caused the failure.</param>
    public OllamaConnectionException(string message, Exception? innerException = null)
        : base(message, isRetryable: true, providerName: "Ollama",
               errorCode: "connection_refused", httpStatusCode: null, innerException)
    {
    }
}
