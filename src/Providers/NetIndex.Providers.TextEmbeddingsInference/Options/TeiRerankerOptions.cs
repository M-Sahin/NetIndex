namespace NetIndex.Providers.TextEmbeddingsInference.Options;

/// <summary>Options for the Text Embeddings Inference reranker provider.</summary>
public sealed class TeiRerankerOptions
{
    /// <summary>Gets or sets the TEI base URL. Default: <c>http://localhost:8080</c>. It must not carry a query or fragment.</summary>
    public string Endpoint { get; set; } = "http://localhost:8080";

    /// <summary>Gets or sets the TCP connect timeout. Default: 500 milliseconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets or sets the total request timeout. Default: 5 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the number of consecutive failures that opens the circuit. Default: 3.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>Gets or sets how long the circuit stays open before a probe is allowed. Default: 30 seconds.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether the provider connects only to loopback, RFC 1918, IPv6 unique-local or link-local
    /// addresses. When true the host name is resolved at connect time and a connection to any other address is
    /// refused (retryable <c>non_private_address</c>) before any request is sent. Default: false.
    /// </summary>
    public bool RestrictToPrivateNetwork { get; set; }
}
