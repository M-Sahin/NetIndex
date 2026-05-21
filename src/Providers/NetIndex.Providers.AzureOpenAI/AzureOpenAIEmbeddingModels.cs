using NetIndex.Core.Abstractions;

namespace NetIndex.Providers.AzureOpenAI;

internal static class AzureOpenAIEmbeddingModels
{
    private static readonly IReadOnlyDictionary<string, int> KnownDimensions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["text-embedding-3-small"] = 1536,
        ["text-embedding-3-large"] = 3072,
        ["text-embedding-ada-002"] = 1536,
    };

    public static int ResolveDimensions(string deployment, int? configuredDimensions)
    {
        if (configuredDimensions is { } dimensions)
        {
            return dimensions;
        }
        if (KnownDimensions.TryGetValue(deployment, out var nativeDimensions))
        {
            return nativeDimensions;
        }
        throw new NetIndexConfigurationException(
            $"Azure OpenAI embedding dimensions could not be inferred for deployment '{deployment}'. Set AzureOpenAI:EmbeddingDimensions explicitly.",
            "EmbeddingDimensions",
            "A positive dimension count",
            null);
    }
}
