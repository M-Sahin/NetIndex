using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
using NetIndex.Providers.AzureOpenAI.Options;
using OpenAI.Embeddings;

namespace NetIndex.Providers.AzureOpenAI;

/// <summary>
/// Generates embeddings using Azure OpenAI via the Azure SDK.
/// </summary>
public sealed class AzureOpenAIEmbeddingGenerator : IEmbeddingGenerator, IAsyncDisposable
{
    private readonly AzureOpenAIClient? _azureClient;
    private readonly EmbeddingClient _embeddingClient;
    private readonly string? _deploymentName;
    private readonly int _dimensions;
    private readonly int? _embeddingDimensions;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureOpenAIEmbeddingGenerator"/> class.
    /// </summary>
    /// <param name="options">Resolved Azure OpenAI embedding options.</param>
    public AzureOpenAIEmbeddingGenerator(IOptions<AzureOpenAIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opt = options.Value;
        ArgumentNullException.ThrowIfNull(opt.Endpoint);

        _deploymentName = opt.EmbeddingDeployment;
        _dimensions = AzureOpenAIEmbeddingModels.ResolveDimensions(opt.EmbeddingDeployment, opt.EmbeddingDimensions);
        _embeddingDimensions = opt.EmbeddingDimensions;
        try
        {
            _azureClient = AzureOpenAIProviderHelpers.CreateClient(
                opt.Endpoint,
                opt.ApiKey,
                opt.Credential,
                opt.ApiVersion,
                opt.Timeout);
            _embeddingClient = _azureClient.GetEmbeddingClient(opt.EmbeddingDeployment);
        }
        catch
        {
            DisposeClient(_azureClient);
            throw;
        }
    }

    internal AzureOpenAIEmbeddingGenerator(EmbeddingClient embeddingClient, int dimensions, int? embeddingDimensions = null)
    {
        ArgumentNullException.ThrowIfNull(embeddingClient);
        _embeddingClient = embeddingClient;
        _dimensions = dimensions;
        _embeddingDimensions = embeddingDimensions;
    }

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var activity = NetIndexActivitySource.Source.StartActivity("AzureOpenAI.GenerateEmbedding");
        activity?.SetTag("azure.openai.deployment", _deploymentName);
        try
        {
            var result = await _embeddingClient.GenerateEmbeddingAsync(
                text,
                CreateOptions(),
                cancellationToken).ConfigureAwait(false);
            var vector = result.Value.ToFloats().ToArray();
            // Patch #9: validate returned vector length matches the provider Dimensions contract.
            if (vector.Length != _dimensions)
            {
                throw new NetIndexProviderException(
                    $"Azure OpenAI returned a {vector.Length}-dimension vector, but {Dimensions} dimensions are configured.",
                    isRetryable: false,
                    providerName: AzureOpenAIProviderHelpers.ProviderName,
                    errorCode: "dimension_mismatch",
                    httpStatusCode: null,
                    innerException: null);
            }

            return vector;
        }
        catch (Exception ex)
        {
            throw AzureOpenAIProviderHelpers.Wrap(ex, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<float[][]> GenerateBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Patch #8: fail-fast on empty batch — avoid unnecessary Azure round-trip.
        var textArray = texts as string[] ?? texts.ToArray();
        if (textArray.Length == 0)
        {
            return Array.Empty<float[]>();
        }

        using var activity = NetIndexActivitySource.Source.StartActivity("AzureOpenAI.GenerateBatch");
        activity?.SetTag("azure.openai.deployment", _deploymentName);
        try
        {
            var result = await _embeddingClient.GenerateEmbeddingsAsync(
                textArray,
                CreateOptions(),
                cancellationToken).ConfigureAwait(false);
            var vectors = result.Value.Select(embedding => embedding.ToFloats().ToArray()).ToArray();
            // Patch #9: validate each vector length against the provider Dimensions contract.
            for (int i = 0; i < vectors.Length; i++)
            {
                if (vectors[i].Length != _dimensions)
                {
                    throw new NetIndexProviderException(
                        $"Azure OpenAI returned a {vectors[i].Length}-dimension vector at index {i}, but {Dimensions} dimensions are configured.",
                        isRetryable: false,
                        providerName: AzureOpenAIProviderHelpers.ProviderName,
                        errorCode: "dimension_mismatch",
                        httpStatusCode: null,
                        innerException: null);
                }
            }

            return vectors;
        }
        catch (Exception ex)
        {
            throw AzureOpenAIProviderHelpers.Wrap(ex, cancellationToken);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        return DisposeClientsAsync();
    }

    private EmbeddingGenerationOptions? CreateOptions() => _embeddingDimensions is { } dimensions
        ? new EmbeddingGenerationOptions { Dimensions = dimensions }
        : null;

    private async ValueTask DisposeClientsAsync()
    {
        try
        {
            await DisposeClientAsync(_embeddingClient).ConfigureAwait(false);
        }
        finally
        {
            await DisposeClientAsync(_azureClient).ConfigureAwait(false);
        }
    }

    private static void DisposeClient(object? client)
    {
        if (client is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static async ValueTask DisposeClientAsync(object? client)
    {
        switch (client)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
