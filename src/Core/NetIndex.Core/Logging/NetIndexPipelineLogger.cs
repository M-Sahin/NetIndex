#pragma warning disable CS1591
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NetIndex.Core.Abstractions;
using NetIndex.Core.Abstractions.Telemetry;

namespace NetIndex.Core.Logging;

/// <summary>
/// Structured log emission helpers for <see cref="NetIndexPipeline"/>.
/// Builds explicit <c>IReadOnlyList&lt;KeyValuePair&lt;string, object?&gt;&gt;</c> state so
/// JSON/structured providers enumerate stable dot-notation field names that match
/// <see cref="NetIndexSpanTags"/> for log/span correlation.
/// </summary>
internal static class NetIndexPipelineLogger
{
    private static readonly Func<List<KeyValuePair<string, object?>>, Exception?, string> Formatter =
        static (state, _) =>
        {
            string? op = null, status = null;
            object? ms = null;
            foreach (var kv in state)
            {
                if (kv.Key == NetIndexLogFields.Operation)
                {
                    op = kv.Value as string;
                }
                else if (kv.Key == NetIndexLogFields.Status)
                {
                    status = kv.Value as string;
                }
                else if (kv.Key == NetIndexLogFields.DurationMs)
                {
                    ms = kv.Value;
                }
            }
            return $"NetIndex {op} {status} [{ms}ms]";
        };

    // ── Ingest ──

    public static void LogIngestSucceeded(
        ILogger logger, long durationMs, string tenantId, string documentId,
        int chunkCount, int embeddingCount, int embeddingDimensions)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var state = new List<KeyValuePair<string, object?>>
        {
            new(NetIndexLogFields.Operation, NetIndexLogOperations.Ingest),
            new(NetIndexLogFields.Status, NetIndexLogStatus.Succeeded),
            new(NetIndexLogFields.DurationMs, durationMs),
            new(NetIndexSpanTags.TenantId, tenantId),
            new(NetIndexSpanTags.DocumentId, documentId),
            new(NetIndexSpanTags.ChunkCount, chunkCount),
            new(NetIndexSpanTags.EmbeddingCount, embeddingCount),
            new(NetIndexSpanTags.EmbeddingDimensions, embeddingDimensions),
            new("{OriginalFormat}", "NetIndex ingest {netindex.status} [{netindex.duration_ms}ms]"),
        };
        logger.Log(LogLevel.Information, NetIndexLogEventIds.IngestSucceeded, state, null, Formatter);
    }

    public static void LogIngestFailed(
        ILogger logger, long durationMs, string? tenantId, Exception exception)
    {
        var isCancel = exception is OperationCanceledException;
        var level = isCancel ? LogLevel.Information : LogLevel.Error;
        var status = isCancel ? NetIndexLogStatus.Canceled : NetIndexLogStatus.Failed;
        if (!logger.IsEnabled(level))
        {
            return;
        }

        var state = BuildFailureState(NetIndexLogOperations.Ingest, status, durationMs, tenantId, exception);
        logger.Log(level, NetIndexLogEventIds.IngestFailed, state, exception, Formatter);
    }

    // ── Query ──

    public static void LogQuerySucceeded(
        ILogger logger, long durationMs, string tenantId,
        int embeddingDimensions, int retrieveTop, int resultCount, int filteredCount)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var state = new List<KeyValuePair<string, object?>>
        {
            new(NetIndexLogFields.Operation, NetIndexLogOperations.Query),
            new(NetIndexLogFields.Status, NetIndexLogStatus.Succeeded),
            new(NetIndexLogFields.DurationMs, durationMs),
            new(NetIndexSpanTags.TenantId, tenantId),
            new(NetIndexSpanTags.EmbeddingDimensions, embeddingDimensions),
            new(NetIndexSpanTags.RetrieveTop, retrieveTop),
            new(NetIndexSpanTags.RetrieveResultCount, resultCount),
            new(NetIndexSpanTags.RetrieveFilteredCount, filteredCount),
            new("{OriginalFormat}", "NetIndex query {netindex.status} [{netindex.duration_ms}ms]"),
        };
        logger.Log(LogLevel.Information, NetIndexLogEventIds.QuerySucceeded, state, null, Formatter);
    }

    public static void LogQueryFailed(
        ILogger logger, long durationMs, string? tenantId, Exception exception)
    {
        var isCancel = exception is OperationCanceledException;
        var level = isCancel ? LogLevel.Information : LogLevel.Error;
        var status = isCancel ? NetIndexLogStatus.Canceled : NetIndexLogStatus.Failed;
        if (!logger.IsEnabled(level))
        {
            return;
        }

        var state = BuildFailureState(NetIndexLogOperations.Query, status, durationMs, tenantId, exception);
        logger.Log(level, NetIndexLogEventIds.QueryFailed, state, exception, Formatter);
    }

    // ── Generate ──

    public static void LogGenerateSucceeded(
        ILogger logger, long durationMs, string tenantId,
        int embeddingDimensions, int retrieveTop, int resultCount, int filteredCount, int contextChunkCount)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var state = new List<KeyValuePair<string, object?>>
        {
            new(NetIndexLogFields.Operation, NetIndexLogOperations.Generate),
            new(NetIndexLogFields.Status, NetIndexLogStatus.Succeeded),
            new(NetIndexLogFields.DurationMs, durationMs),
            new(NetIndexSpanTags.TenantId, tenantId),
            new(NetIndexSpanTags.EmbeddingDimensions, embeddingDimensions),
            new(NetIndexSpanTags.RetrieveTop, retrieveTop),
            new(NetIndexSpanTags.RetrieveResultCount, resultCount),
            new(NetIndexSpanTags.RetrieveFilteredCount, filteredCount),
            new(NetIndexSpanTags.ContextChunkCount, contextChunkCount),
            new("{OriginalFormat}", "NetIndex generate {netindex.status} [{netindex.duration_ms}ms]"),
        };
        logger.Log(LogLevel.Information, NetIndexLogEventIds.GenerateSucceeded, state, null, Formatter);
    }

    public static void LogGenerateCanceled(
        ILogger logger, long durationMs, string? tenantId)
    {
        if (!logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        var state = new List<KeyValuePair<string, object?>>
        {
            new(NetIndexLogFields.Operation, NetIndexLogOperations.Generate),
            new(NetIndexLogFields.Status, NetIndexLogStatus.Canceled),
            new(NetIndexLogFields.DurationMs, durationMs),
        };
        if (tenantId is not null)
        {
            state.Add(new(NetIndexSpanTags.TenantId, tenantId));
        }

        state.Add(new("{OriginalFormat}", "NetIndex generate {netindex.status} [{netindex.duration_ms}ms]"));
        logger.Log(LogLevel.Information, NetIndexLogEventIds.GenerateFailed, state, null, Formatter);
    }

    public static void LogGenerateFailed(
        ILogger logger, long durationMs, string? tenantId, Exception exception)
    {
        var isCancel = exception is OperationCanceledException;
        var level = isCancel ? LogLevel.Information : LogLevel.Error;
        var status = isCancel ? NetIndexLogStatus.Canceled : NetIndexLogStatus.Failed;
        if (!logger.IsEnabled(level))
        {
            return;
        }

        var state = BuildFailureState(NetIndexLogOperations.Generate, status, durationMs, tenantId, exception);
        logger.Log(level, NetIndexLogEventIds.GenerateFailed, state, exception, Formatter);
    }

    // ── Shared failure state builder ──

    private static List<KeyValuePair<string, object?>> BuildFailureState(
        string operation, string status, long durationMs, string? tenantId, Exception exception)
    {
        var state = new List<KeyValuePair<string, object?>>
        {
            new(NetIndexLogFields.Operation, operation),
            new(NetIndexLogFields.Status, status),
            new(NetIndexLogFields.DurationMs, durationMs),
            new(NetIndexLogFields.ExceptionType, exception.GetType().FullName),
            new(NetIndexLogFields.ExceptionMessage, exception.Message),
        };

        if (tenantId is not null)
        {
            state.Add(new(NetIndexSpanTags.TenantId, tenantId));
        }

        if (exception is NetIndexAuthorizationException authEx)
        {
            if (authEx.FailureReason is not null)
            {
                state.Add(new(NetIndexLogFields.FailureReason, authEx.FailureReason));
            }
        }
        else if (exception is NetIndexProviderException provEx)
        {
            if (provEx.ErrorCode is not null)
            {
                state.Add(new(NetIndexLogFields.ErrorCode, provEx.ErrorCode));
            }

            if (provEx.ProviderName is not null)
            {
                state.Add(new(NetIndexLogFields.ProviderName, provEx.ProviderName));
            }

            if (provEx.HttpStatusCode is not null)
            {
                state.Add(new(NetIndexLogFields.HttpStatusCode, provEx.HttpStatusCode));
            }
        }
        else if (exception is NetIndexStorageException storeEx)
        {
            if (storeEx.StoreName is not null)
            {
                state.Add(new(NetIndexLogFields.StoreName, storeEx.StoreName));
            }

            if (storeEx.Operation is not null)
            {
                state.Add(new(NetIndexLogFields.StorageOperation, storeEx.Operation));
            }
        }

        state.Add(new("{OriginalFormat}", $"NetIndex {operation} {{netindex.status}} [{{netindex.duration_ms}}ms]"));
        return state;
    }
}
