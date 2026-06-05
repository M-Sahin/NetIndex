using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace NetIndex.Core.Logging;

/// <summary>Log-only structured field keys (span-tag keys come from NetIndexSpanTags).</summary>
internal static class NetIndexLogFields
{
    public const string Operation = "netindex.operation";
    public const string Status = "netindex.status";
    public const string DurationMs = "netindex.duration_ms";
    public const string ExceptionType = "exception.type";
    public const string ExceptionMessage = "exception.message";
    public const string ErrorCode = "error_code";
    public const string FailureReason = "failure_reason";
    public const string ProviderName = "provider_name";
    public const string HttpStatusCode = "http_status_code";
    public const string StoreName = "store_name";
    public const string StorageOperation = "storage_operation";
}

internal static class NetIndexLogStatus
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}

internal static class NetIndexLogOperations
{
    public const string Ingest = "ingest";
    public const string Query = "query";
    public const string Generate = "generate";
}

/// <summary>
/// Stable EventId scheme: success = 100x, failure = 110x (success id + 100).
/// </summary>
internal static class NetIndexLogEventIds
{
    public static readonly EventId IngestSucceeded = new(1001, "IngestSucceeded");
    public static readonly EventId QuerySucceeded = new(1002, "QuerySucceeded");
    public static readonly EventId GenerateSucceeded = new(1003, "GenerateSucceeded");
    public static readonly EventId IngestFailed = new(1101, "IngestFailed");
    public static readonly EventId QueryFailed = new(1102, "QueryFailed");
    public static readonly EventId GenerateFailed = new(1103, "GenerateFailed");
}
