using Microsoft.Extensions.Options;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;
using NetIndex.Providers.OpenAI.Options;
using OpenAI;
using OpenAI.Embeddings;

namespace NetIndex.Providers.OpenAI;

/// <summary>
/// Generates embeddings using the standard OpenAI API via the official .NET SDK.
/// </summary>
public sealed class OpenAIEmbeddingGenerator : IEmbeddingGenerator, IAsyncDisposable
{
    private readonly OpenAIClient? _openAIClient;
    private readonly EmbeddingClient _embeddingClient;
    private readonly string _modelName;
    private readonly int _dimensions;
    private readonly int? _embeddingDimensions;
    private int _disposeState;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIEmbeddingGenerator"/> class.
    /// </summary>
    /// <param name="options">Resolved OpenAI options.</param>
    public OpenAIEmbeddingGenerator(IOptions<OpenAIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opt = options.Value;

        _modelName = opt.EmbeddingModel;
        _dimensions = OpenAIEmbeddingModels.ResolveDimensions(opt.EmbeddingModel, opt.EmbeddingDimensions);
        _embeddingDimensions = opt.EmbeddingDimensions;
        try
        {
            _openAIClient = OpenAIProviderHelpers.CreateClient(opt.ApiKey, opt.Endpoint, opt.Timeout);
            _embeddingClient = _openAIClient.GetEmbeddingClient(opt.EmbeddingModel);
        }
        catch
        {
            DisposeClient(_openAIClient);
            throw;
        }
    }

    internal OpenAIEmbeddingGenerator(EmbeddingClient embeddingClient, int dimensions, int? embeddingDimensions = null)
    {
        ArgumentNullException.ThrowIfNull(embeddingClient);
        _embeddingClient = embeddingClient;
        _modelName = string.Empty;
        _dimensions = dimensions;
        _embeddingDimensions = embeddingDimensions;
    }

    /// <inheritdoc />
    public int Dimensions => _dimensions;

    /// <inheritdoc />
    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);

        using var activity = NetIndexActivitySource.Source.StartActivity("OpenAI.GenerateEmbedding");
        activity?.SetTag("openai.model", _modelName);
        try
        {
            var result = await _embeddingClient.GenerateEmbeddingAsync(
                text,
                CreateOptions(),
                cancellationToken).ConfigureAwait(false);
            var vector = result.Value.ToFloats().ToArray();
            if (vector.Length != _dimensions)
            {
                throw new NetIndexProviderException(
                    $"OpenAI returned a {vector.Length}-dimension vector, but {Dimensions} dimensions are configured.",
                    isRetryable: false,
                    providerName: OpenAIProviderHelpers.ProviderName,
                    errorCode: "dimension_mismatch",
                    httpStatusCode: null,
                    innerException: null);
            }
            return vector;
        }
        catch (Exception ex)
        {
            var wrapped = OpenAIProviderHelpers.Wrap(ex, cancellationToken);
            if (OpenAIProviderHelpers.ShouldRethrowOriginal(ex, wrapped))
            {
                throw;
            }
            throw OpenAIProviderHelpers.ThrowPreserveContext(wrapped);
        }
    }

    /// <inheritdoc />
    public async Task<float[][]> GenerateBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ObjectDisposedException.ThrowIf(_disposeState != 0, this);

        var textArray = texts as string[] ?? texts.ToArray();
        if (textArray.Length == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Array.Empty<float[]>();
        }

        using var activity = NetIndexActivitySource.Source.StartActivity("OpenAI.GenerateBatch");
        activity?.SetTag("openai.model", _modelName);
        try
        {
            var result = await _embeddingClient.GenerateEmbeddingsAsync(
                textArray,
                CreateOptions(),
                cancellationToken).ConfigureAwait(false);
            var embeddings = result.Value;
            if (embeddings.Count != textArray.Length)
            {
                throw new NetIndexProviderException(
                    $"OpenAI returned {embeddings.Count} embeddings for {textArray.Length} inputs.",
                    isRetryable: false,
                    providerName: OpenAIProviderHelpers.ProviderName,
                    errorCode: "invalid_response",
                    httpStatusCode: null,
                    innerException: null);
            }
            var ordered = embeddings.OrderBy(e => e.Index).ToArray();
            var vectors = new float[ordered.Length][];
            for (int i = 0; i < ordered.Length; i++)
            {
                if (ordered[i].Index != i)
                {
                    // Count matched but indices are not the contiguous set [0, count): a duplicate
                    // and a missing index would otherwise be masked by OrderBy and silently
                    // mis-align vectors to their source texts.
                    throw new NetIndexProviderException(
                        $"OpenAI returned embeddings with non-contiguous indices; expected index {i} but found {ordered[i].Index}.",
                        isRetryable: false,
                        providerName: OpenAIProviderHelpers.ProviderName,
                        errorCode: "invalid_response",
                        httpStatusCode: null,
                        innerException: null);
                }
                var vector = ordered[i].ToFloats().ToArray();
                if (vector.Length != _dimensions)
                {
                    throw new NetIndexProviderException(
                        $"OpenAI returned a {vector.Length}-dimension vector at index {i}, but {Dimensions} dimensions are configured.",
                        isRetryable: false,
                        providerName: OpenAIProviderHelpers.ProviderName,
                        errorCode: "dimension_mismatch",
                        httpStatusCode: null,
                        innerException: null);
                }
                vectors[i] = vector;
            }
            return vectors;
        }
        catch (Exception ex)
        {
            var wrapped = OpenAIProviderHelpers.Wrap(ex, cancellationToken);
            if (OpenAIProviderHelpers.ShouldRethrowOriginal(ex, wrapped))
            {
                throw;
            }
            throw OpenAIProviderHelpers.ThrowPreserveContext(wrapped);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }
        return DisposeClientsAsync();
    }

    private EmbeddingGenerationOptions? CreateOptions() => _embeddingDimensions is { } dims
        ? new EmbeddingGenerationOptions { Dimensions = dims }
        : null;

    private async ValueTask DisposeClientsAsync()
    {
        try
        {
            await DisposeClientAsync(_embeddingClient).ConfigureAwait(false);
        }
        finally
        {
            await DisposeClientAsync(_openAIClient).ConfigureAwait(false);
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
