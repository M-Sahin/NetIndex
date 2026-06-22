using System.ClientModel;
using System.Net.Sockets;
using NetIndex.Core.Abstractions;
using OpenAI;

namespace NetIndex.Providers.OpenAI;

internal static class OpenAIProviderHelpers
{
    public const string ProviderName = "OpenAI";

    public static OpenAIClient CreateClient(string apiKey, Uri? endpoint, TimeSpan timeout)
    {
        var options = new OpenAIClientOptions
        {
            NetworkTimeout = timeout,
        };
        if (endpoint is not null)
        {
            options.Endpoint = endpoint;
        }
        return new OpenAIClient(new ApiKeyCredential(apiKey), options);
    }

    public static Exception Wrap(Exception ex, CancellationToken callerToken)
    {
        // Caller cancellation must never be wrapped. Check whether the caller's token
        // is the one that fired — regardless of which token the SDK placed on the OCE
        // (the SDK may use an internal linked CTS, so token-equality checks are fragile).
        if (ex is OperationCanceledException && callerToken.IsCancellationRequested)
        {
            return ex;
        }

        return ex switch
        {
            // Non-caller OperationCanceledException is an SDK-internal timeout.
            OperationCanceledException => new NetIndexProviderException(
                "OpenAI request timed out.",
                isRetryable: true,
                providerName: ProviderName,
                errorCode: "timeout",
                httpStatusCode: null,
                innerException: ex),

            // The standard OpenAI SDK surfaces ALL HTTP errors as ClientResultException with .Status.
            // Unlike Azure (which uses RequestFailedException for HTTP status), reading .Status here
            // is the correct classification path — only fall back to client_result when Status == 0.
            ClientResultException cre when cre.Status is 401 or 403 => new OpenAIAuthenticationException(
                $"OpenAI authentication failed (HTTP {cre.Status}): {cre.Message}",
                cre,
                "auth_failed",
                cre.Status),
            ClientResultException cre when cre.Status == 408 => CreateHttpException(cre, true, "http_408"),
            ClientResultException cre when cre.Status == 429 => CreateHttpException(cre, true, "rate_limited"),
            ClientResultException cre when cre.Status == 501 => CreateHttpException(cre, false, "http_501"),
            ClientResultException cre when cre.Status >= 500 => CreateHttpException(cre, true, $"http_{cre.Status}"),
            ClientResultException cre when cre.Status >= 400 => CreateHttpException(cre, false, $"http_{cre.Status}"),
            ClientResultException cre when cre.Status == 0 => new NetIndexProviderException(
                $"OpenAI SDK client error: {cre.Message}",
                isRetryable: false,
                providerName: ProviderName,
                errorCode: "client_result",
                httpStatusCode: null,
                innerException: cre),
            ClientResultException cre => CreateHttpException(cre, false, $"http_{cre.Status}"),

            SocketException or IOException or HttpRequestException => new NetIndexProviderException(
                $"OpenAI network failure: {ex.Message}",
                isRetryable: true,
                providerName: ProviderName,
                errorCode: "network",
                httpStatusCode: null,
                innerException: ex),

            // Already-classified NetIndex exceptions must not be re-wrapped by callers that
            // route all exceptions through Wrap (e.g., empty-batch or dimension checks inside a try).
            NetIndexProviderException or NetIndexConfigurationException => ex,

            _ => new NetIndexProviderException(
                $"OpenAI provider raised an unexpected error: {ex.Message}",
                isRetryable: false,
                providerName: ProviderName,
                errorCode: "provider_error",
                httpStatusCode: null,
                innerException: ex),
        };
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public static Exception ThrowPreserveContext(Exception ex) => throw ex;

    public static bool ShouldRethrowOriginal(Exception ex, Exception wrapped) => ReferenceEquals(ex, wrapped);

    private static NetIndexProviderException CreateHttpException(ClientResultException ex, bool retryable, string errorCode)
        => new(
            $"OpenAI returned HTTP {ex.Status}: {ex.Message}",
            retryable,
            ProviderName,
            errorCode,
            ex.Status == 0 ? null : ex.Status,
            ex);
}
