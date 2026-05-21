using Microsoft.Extensions.Options;

namespace NetIndex.Providers.AzureOpenAI.Options;

internal sealed class AzureOpenAIChatOptionsValidator : IValidateOptions<AzureOpenAIChatOptions>
{
    public ValidateOptionsResult Validate(string? name, AzureOpenAIChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        ValidateEndpoint(options.Endpoint, failures, nameof(AzureOpenAIChatOptions.Endpoint));
        if (string.IsNullOrWhiteSpace(options.ChatDeployment))
        {
            failures.Add("AzureOpenAIChatOptions.ChatDeployment is required.");
        }
        if (options.Timeout <= TimeSpan.Zero)
        {
            failures.Add("AzureOpenAIChatOptions.Timeout must be greater than TimeSpan.Zero.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateEndpoint(Uri? endpoint, ICollection<string> failures, string propertyName)
    {
        if (endpoint is null)
        {
            failures.Add($"AzureOpenAIChatOptions.{propertyName} is required.");
            return;
        }
        if (!endpoint.IsAbsoluteUri)
        {
            failures.Add($"AzureOpenAIChatOptions.{propertyName} must be an absolute URI.");
        }
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"AzureOpenAIChatOptions.{propertyName} must use HTTPS.");
        }
    }
}
