using NetIndex.Core.Abstractions;

namespace NetIndex.Providers.OpenAI;

internal static class OpenAIEmbeddingModels
{
    private static readonly IReadOnlyDictionary<string, int> KnownDimensions =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["text-embedding-3-small"] = 1536,
            ["text-embedding-3-large"] = 3072,
            ["text-embedding-ada-002"] = 1536,
        };

    public static int ResolveDimensions(string model, int? configuredDimensions)
    {
        if (KnownDimensions.TryGetValue(model, out var nativeDimensions))
        {
            if (configuredDimensions is { } dimensions && dimensions > nativeDimensions)
            {
                throw new NetIndexConfigurationException(
                    $"OpenAI:EmbeddingDimensions {dimensions} exceeds the maximum supported by model '{model}' ({nativeDimensions}). " +
                    $"Use a value from 1 to {nativeDimensions}, or omit to use the model default.",
                    "EmbeddingDimensions",
                    $"An integer from 1 to {nativeDimensions}",
                    null);
            }
            return configuredDimensions ?? nativeDimensions;
        }

        if (configuredDimensions is { } explicitDimensions)
        {
            return explicitDimensions;
        }

        throw new NetIndexConfigurationException(
            $"OpenAI embedding dimensions could not be inferred for model '{model}'. Set OpenAI:EmbeddingDimensions explicitly.",
            "EmbeddingDimensions",
            "A positive dimension count",
            null);
    }
}
