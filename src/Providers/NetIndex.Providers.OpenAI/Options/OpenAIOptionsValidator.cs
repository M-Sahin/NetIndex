using Microsoft.Extensions.Options;

namespace NetIndex.Providers.OpenAI.Options;

internal sealed class OpenAIOptionsValidator : IValidateOptions<OpenAIOptions>
{
    public ValidateOptionsResult Validate(string? name, OpenAIOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("OpenAIOptions.ApiKey is required.");
        }

        if (options.Endpoint is not null)
        {
            if (!options.Endpoint.IsAbsoluteUri)
            {
                failures.Add("OpenAIOptions.Endpoint must be an absolute URI.");
            }
            else if (!string.Equals(options.Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("OpenAIOptions.Endpoint must use HTTPS.");
            }
        }

        if (string.IsNullOrWhiteSpace(options.EmbeddingModel))
        {
            failures.Add("OpenAIOptions.EmbeddingModel is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ChatModel))
        {
            failures.Add("OpenAIOptions.ChatModel is required.");
        }

        if (options.EmbeddingDimensions is <= 0)
        {
            failures.Add("OpenAIOptions.EmbeddingDimensions must be greater than zero when set.");
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            failures.Add("OpenAIOptions.Timeout must be greater than TimeSpan.Zero.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
