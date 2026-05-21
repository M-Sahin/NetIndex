using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using NetIndex.Core.Abstractions;
using System.ClientModel;

namespace NetIndex.Providers.AzureOpenAI;

internal static class AzureOpenAIProviderHelpers
{
    public const string ProviderName = "AzureOpenAI";

    public static AzureOpenAIClient CreateClient(
        Uri endpoint,
        string? apiKey,
        TokenCredential? credential,
        string? apiVersion,
        TimeSpan timeout)
    {
        var clientOptions = CreateClientOptions(apiVersion, timeout);
        return !string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(endpoint, new ApiKeyCredential(apiKey), clientOptions)
            : new AzureOpenAIClient(endpoint, credential ?? CreateDefaultCredential(), clientOptions);
    }

    public static AzureOpenAIClientOptions CreateClientOptions(string? apiVersion, TimeSpan timeout)
    {
        var version = MapApiVersion(apiVersion);
        var options = version.HasValue
            ? new AzureOpenAIClientOptions(version.Value)
            : new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_10_21);
        options.NetworkTimeout = timeout;
        return options;
    }

    #pragma warning disable CS0618 // ExcludeSharedTokenCacheCredential is deprecated per review finding
    public static TokenCredential CreateDefaultCredential() =>
        new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true,
            ExcludeVisualStudioCredential = true,
            ExcludeVisualStudioCodeCredential = true,
            ExcludeAzurePowerShellCredential = true,
            ExcludeSharedTokenCacheCredential = true,
        });
    #pragma warning restore CS0618

    public static AzureOpenAIClientOptions.ServiceVersion? MapApiVersion(string? apiVersion)
    {
        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            return null;
        }

        return apiVersion.Trim() switch
        {
            "2024-06-01" => AzureOpenAIClientOptions.ServiceVersion.V2024_06_01,
            "2024-10-21" => AzureOpenAIClientOptions.ServiceVersion.V2024_10_21,
            _ => throw new NetIndexConfigurationException(
                $"Unsupported Azure OpenAI API version '{apiVersion}'. Supported values are 2024-06-01 and 2024-10-21.",
                "ApiVersion",
                "2024-06-01 or 2024-10-21",
                apiVersion),
        };
    }

    public static Exception Wrap(Exception ex, CancellationToken callerToken)
    {
        // Caller cancellation: compare tokens before general OCE check so caller-initiated
        // cancellation is never wrapped or misclassified as a retryable timeout.
        if (ex is OperationCanceledException oce && oce.CancellationToken == callerToken)
        {
            return ex;
        }

        return ex switch
        {
            OperationCanceledException => new NetIndexProviderException(
                "Azure OpenAI request timed out.",
                isRetryable: true,
                providerName: ProviderName,
                errorCode: "timeout",
                httpStatusCode: null,
                innerException: ex),
            ClientResultException cre => new NetIndexProviderException(
                $"Azure OpenAI SDK client error: {cre.Message}",
                isRetryable: false,
                providerName: ProviderName,
                errorCode: "client_result",
                httpStatusCode: null,
                innerException: cre),
            RequestFailedException rfe when rfe.Status is 401 or 403 => new AzureOpenAIAuthenticationException(
                $"Azure OpenAI authentication failed (HTTP {rfe.Status}): {rfe.Message}",
                rfe,
                "auth_failed",
                rfe.Status),
            RequestFailedException rfe when rfe.Status == 408 => CreateHttpException(rfe, true, "http_408"),
            RequestFailedException rfe when rfe.Status == 429 => CreateHttpException(rfe, true, "rate_limited"),
            RequestFailedException rfe when rfe.Status is 500 or 502 or 503 or 504 => CreateHttpException(rfe, true, $"http_{rfe.Status}"),
            RequestFailedException rfe when rfe.Status == 501 => CreateHttpException(rfe, false, "http_501"),
            RequestFailedException rfe when rfe.Status >= 400 && rfe.Status < 500 => CreateHttpException(rfe, false, $"http_{rfe.Status}"),
            RequestFailedException rfe => CreateHttpException(rfe, rfe.Status >= 500, $"http_{rfe.Status}"),
            AuthenticationFailedException afe => new AzureOpenAIAuthenticationException(
                $"Azure credential authentication failed: {afe.Message}",
                afe,
                "credential_failed",
                null),
            SocketException or IOException or HttpRequestException => new NetIndexProviderException(
                $"Azure OpenAI network failure: {ex.Message}",
                isRetryable: true,
                providerName: ProviderName,
                errorCode: "network",
                httpStatusCode: null,
                innerException: ex),
            _ => ex,
        };
    }

    /// <summary>
    /// Throws <paramref name="ex"/> using the <c>throw</c> expression to preserve the original
    /// stack trace. Use this instead of <c>throw ex</c> after <see cref="Wrap"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public static Exception ThrowPreserveContext(Exception ex)
    {
        throw ex;
    }

    public static bool ShouldRethrowOriginal(Exception ex, Exception wrapped) => ReferenceEquals(ex, wrapped);

    private static NetIndexProviderException CreateHttpException(RequestFailedException ex, bool retryable, string errorCode)
    {
        var retryAfter = ex.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true
            ? $" Retry-After: {value}."
            : string.Empty;
        return new NetIndexProviderException(
            $"Azure OpenAI returned HTTP {ex.Status}: {ex.Message}.{retryAfter}",
            retryable,
            ProviderName,
            errorCode,
            ex.Status,
            ex);
    }
}
