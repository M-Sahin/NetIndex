using Microsoft.Extensions.Options;

namespace NetIndex.Providers.Ollama.Options;

/// <summary>Validates <see cref="OllamaChatOptions"/> at build time.</summary>
public sealed class OllamaChatOptionsValidator : IValidateOptions<OllamaChatOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OllamaChatOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return ValidateOptionsResult.Fail("OllamaChatOptions.Endpoint must not be empty.");
        }
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail("OllamaChatOptions.Endpoint must be a valid absolute URI.");
        }
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            return ValidateOptionsResult.Fail("OllamaChatOptions.Model must not be empty.");
        }
        if (options.Timeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("OllamaChatOptions.Timeout must be positive.");
        }
        return ValidateOptionsResult.Success;
    }
}
