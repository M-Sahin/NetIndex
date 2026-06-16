namespace NetIndex.Providers.OpenAI.Options;

/// <summary>
/// Configuration for the standard OpenAI embedding and chat-completion providers.
/// </summary>
public sealed class OpenAIOptions
{
    /// <summary>
    /// Gets or sets the OpenAI API key.
    /// </summary>
    /// <remarks>
    /// Must be kept secret. Use environment variables, user secrets, or a host secret manager —
    /// never embed keys in source code or checked-in configuration.
    /// </remarks>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional custom HTTPS endpoint for OpenAI-compatible services.
    /// </summary>
    /// <remarks>
    /// When null, the standard OpenAI API endpoint (<c>https://api.openai.com/v1</c>) is used.
    /// Custom endpoints must be absolute HTTPS URIs (for example, a self-hosted OpenAI-compatible server).
    /// Azure OpenAI deployments should use <c>NetIndex.Providers.AzureOpenAI</c> instead.
    /// </remarks>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the embedding model name.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>text-embedding-3-small</c> (1536 dimensions).
    /// Supported models with automatic dimension inference:
    /// <c>text-embedding-3-small</c>, <c>text-embedding-3-large</c>, <c>text-embedding-ada-002</c>.
    /// </remarks>
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Gets or sets the chat completion model name.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>gpt-4o-mini</c>.
    /// </remarks>
    public string ChatModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Gets or sets an optional embedding dimensions override for embedding-v3 shortening.
    /// </summary>
    /// <remarks>
    /// Supported only by <c>text-embedding-3-small</c> and <c>text-embedding-3-large</c>.
    /// When set, overrides the model's native dimension and enables shorter vectors.
    /// Must be a positive integer.
    /// </remarks>
    public int? EmbeddingDimensions { get; set; }

    /// <summary>
    /// Gets or sets the network timeout applied to SDK requests.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
}
