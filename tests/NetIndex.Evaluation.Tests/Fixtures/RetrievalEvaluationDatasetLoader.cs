using System.Text.Json;

namespace NetIndex.Evaluation.Tests.Fixtures;

/// <summary>
/// Deserializes and validates a <see cref="RetrievalEvaluationDataset"/> from JSON.
/// </summary>
internal static class RetrievalEvaluationDatasetLoader
{
    private const int MaxTopK = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static RetrievalEvaluationDataset LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return LoadFromJson(File.ReadAllText(path));
    }

    public static RetrievalEvaluationDataset LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        RetrievalEvaluationDataset? dataset;
        try
        {
            dataset = JsonSerializer.Deserialize<RetrievalEvaluationDataset>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Evaluation dataset is not valid JSON.", ex);
        }

        if (dataset is null)
        {
            throw new InvalidDataException("Evaluation dataset deserialized to null.");
        }

        Validate(dataset);
        return dataset;
    }

    /// <summary>
    /// Builds an ordinal chunk-id-to-grade lookup for one query, rejecting duplicate chunk IDs rather than
    /// silently letting a later JSON entry overwrite an earlier one.
    /// </summary>
    public static IReadOnlyDictionary<string, int> BuildJudgmentLookup(RetrievalEvaluationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Relevance is null)
        {
            throw new InvalidDataException($"Query '{query.Id}' has a null relevance collection.");
        }

        var lookup = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var judgment in query.Relevance)
        {
            if (judgment is null)
            {
                throw new InvalidDataException($"Query '{query.Id}' has a null relevance judgment entry.");
            }

            if (string.IsNullOrWhiteSpace(judgment.ChunkId))
            {
                throw new InvalidDataException($"Query '{query.Id}' has a relevance judgment with a null or empty chunk Id.");
            }

            if (!lookup.TryAdd(judgment.ChunkId, judgment.Grade))
            {
                throw new InvalidDataException(
                    $"Query '{query.Id}' has a duplicate relevance judgment for chunk '{judgment.ChunkId}'.");
            }
        }

        return lookup;
    }

    private static void Validate(RetrievalEvaluationDataset dataset)
    {
        if (dataset.Documents is null || dataset.Documents.Count == 0)
        {
            throw new InvalidDataException("Evaluation dataset must contain at least one document.");
        }

        if (dataset.Queries is null || dataset.Queries.Count == 0)
        {
            throw new InvalidDataException("Evaluation dataset must contain at least one query.");
        }

        if (dataset.TopK <= 0 || dataset.TopK > MaxTopK)
        {
            throw new InvalidDataException(
                $"Dataset topK must be in [1, {MaxTopK}] (the pipeline's result cap); got {dataset.TopK}.");
        }

        if (dataset.Thresholds is null
            || dataset.Thresholds.MeanReciprocalRank < 0 || dataset.Thresholds.MeanReciprocalRank > 1
            || dataset.Thresholds.MeanNdcgAtK < 0 || dataset.Thresholds.MeanNdcgAtK > 1)
        {
            throw new InvalidDataException("Dataset thresholds must be explicit values in [0, 1].");
        }

        var documentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in dataset.Documents)
        {
            if (document is null)
            {
                throw new InvalidDataException("Evaluation dataset contains a null document entry.");
            }

            if (string.IsNullOrWhiteSpace(document.Id) || string.IsNullOrWhiteSpace(document.Content))
            {
                throw new InvalidDataException("Every document requires a non-empty Id and Content.");
            }

            if (!documentIds.Add(document.Id))
            {
                throw new InvalidDataException($"Duplicate document Id '{document.Id}'.");
            }
        }

        // Pass-through chunking (the production default used by the evaluator) always yields exactly
        // one chunk per document, with Id "{document.Id}_chunk_0" — see NetIndexPipeline.IngestAsync.
        var validChunkIds = new HashSet<string>(
            documentIds.Select(static id => $"{id}_chunk_0"), StringComparer.Ordinal);

        var queryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var query in dataset.Queries)
        {
            if (query is null)
            {
                throw new InvalidDataException("Evaluation dataset contains a null query entry.");
            }

            if (string.IsNullOrWhiteSpace(query.Id) || string.IsNullOrWhiteSpace(query.Text))
            {
                throw new InvalidDataException("Every query requires a non-empty Id and Text.");
            }

            if (!queryIds.Add(query.Id))
            {
                throw new InvalidDataException($"Duplicate query Id '{query.Id}'.");
            }

            if (query.Relevance is null || query.Relevance.Count == 0)
            {
                throw new InvalidDataException($"Query '{query.Id}' must have at least one relevance judgment.");
            }

            var judgmentLookup = BuildJudgmentLookup(query);

            var hasPositiveJudgment = false;
            foreach (var (chunkId, grade) in judgmentLookup)
            {
                if (grade < RelevanceGrade.Min || grade > RelevanceGrade.Max)
                {
                    throw new InvalidDataException(
                        $"Query '{query.Id}' judgment for chunk '{chunkId}' has grade {grade}, outside [{RelevanceGrade.Min}, {RelevanceGrade.Max}].");
                }

                if (!validChunkIds.Contains(chunkId))
                {
                    throw new InvalidDataException(
                        $"Query '{query.Id}' judges unknown chunk '{chunkId}'; no document produces that chunk Id.");
                }

                if (grade >= 1)
                {
                    hasPositiveJudgment = true;
                }
            }

            if (!hasPositiveJudgment)
            {
                throw new InvalidDataException($"Query '{query.Id}' has no positively judged (grade >= 1) chunk.");
            }
        }
    }
}
