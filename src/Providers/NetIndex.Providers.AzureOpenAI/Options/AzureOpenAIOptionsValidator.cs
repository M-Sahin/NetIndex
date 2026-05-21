using Microsoft.Extensions.Options;

namespace NetIndex.Providers.AzureOpenAI.Options;

internal sealed class AzureOpenAIOptionsValidator : IValidateOptions<AzureOpenAIOptions>
{
    public ValidateOptionsResult Validate(string? name, AzureOpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateEndpoint(options.Endpoint, failures, nameof(AzureOpenAIOptions.Endpoint));
        if (string.IsNullOrWhiteSpace(options.EmbeddingDeployment))
        {
            failures.Add("AzureOpenAIOptions.EmbeddingDeployment is required.");
        }
        if (options.Timeout <= TimeSpan.Zero)
        {
            failures.Add("AzureOpenAIOptions.Timeout must be greater than TimeSpan.Zero.");
        }
        if (options.EmbeddingDimensions is <= 0)
        {
            failures.Add("AzureOpenAIOptions.EmbeddingDimensions must be greater than zero when set.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateEndpoint(Uri? endpoint, ICollection<string> failures, string propertyName)
    {
        if (endpoint is null)
        {
            failures.Add($"AzureOpenAIOptions.{propertyName} is required.");
            return;
        }
        if (!endpoint.IsAbsoluteUri)
        {
            failures.Add($"AzureOpenAIOptions.{propertyName} must be an absolute URI.");
        }
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"AzureOpenAIOptions.{propertyName} must use HTTPS.");
        }
    }
}
