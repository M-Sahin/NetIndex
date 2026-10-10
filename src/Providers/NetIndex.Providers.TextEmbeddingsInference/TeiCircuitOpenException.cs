using NetIndex.Core.Abstractions;

namespace NetIndex.Providers.TextEmbeddingsInference;

/// <summary>Thrown without any network call while the reranker circuit breaker is open.</summary>
public sealed class TeiCircuitOpenException : NetIndexProviderException
{
    /// <summary>Initializes with a message.</summary>
    /// <param name="message">The error message.</param>
    public TeiCircuitOpenException(string message)
        : base(message, isRetryable: true, providerName: TeiDocumentReranker.ProviderName,
               errorCode: "circuit_open", httpStatusCode: null, innerException: null)
    {
    }
}
