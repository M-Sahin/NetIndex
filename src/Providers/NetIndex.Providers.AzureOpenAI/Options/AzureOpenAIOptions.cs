using Azure.Core;

namespace NetIndex.Providers.AzureOpenAI.Options;

/// <summary>
/// Configuration for Azure OpenAI embedding generation.
/// </summary>
public sealed class AzureOpenAIOptions
{
    /// <summary>
    /// Gets or sets the HTTPS endpoint for the Azure OpenAI resource.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the Azure OpenAI embedding deployment name.
    /// </summary>
    public string EmbeddingDeployment { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional Azure OpenAI service API version.
    /// </summary>
    /// <remarks>
    /// When unset, the Azure SDK default service version is used.
    /// Supported values in Azure.AI.OpenAI 2.1.0 are <c>2024-06-01</c> and <c>2024-10-21</c>.
    /// </remarks>
    public string? ApiVersion { get; set; }

    /// <summary>
    /// Gets or sets an optional embedding dimensions override.
    /// </summary>
    /// <remarks>
    /// Only newer <c>text-embedding-3-*</c> deployments accept dimensions overrides. Older models
    /// such as <c>text-embedding-ada-002</c> reject this option at request time.
    /// </remarks>
    public int? EmbeddingDimensions { get; set; }

    /// <summary>
    /// Gets or sets an optional API key for deployments that cannot use token credentials.
    /// </summary>
    /// <remarks>
    /// Leave blank to use <see cref="Credential"/> or the provider's managed-identity-friendly
    /// <c>DefaultAzureCredential</c> fallback.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets an optional caller-supplied Azure credential.
    /// </summary>
    /// <remarks>
    /// Production callers should prefer a deterministic credential such as
    /// <c>ManagedIdentityCredential</c>. When unset and <see cref="ApiKey"/> is blank, the provider
    /// uses <c>DefaultAzureCredential</c> with interactive desktop credentials excluded.
    /// </remarks>
    public TokenCredential? Credential { get; set; }

    /// <summary>
    /// Gets or sets the network timeout used by the Azure OpenAI SDK pipeline.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
}
