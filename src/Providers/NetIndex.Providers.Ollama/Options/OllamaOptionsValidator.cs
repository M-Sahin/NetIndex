using Microsoft.Extensions.Options;

namespace NetIndex.Providers.Ollama.Options;

/// <summary>Validates <see cref="OllamaOptions"/> at build time.</summary>
public sealed class OllamaOptionsValidator : IValidateOptions<OllamaOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OllamaOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return ValidateOptionsResult.Fail("OllamaOptions.Endpoint must not be empty.");
        }
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail("OllamaOptions.Endpoint must be a valid absolute URI.");
        }
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            return ValidateOptionsResult.Fail("OllamaOptions.Model must not be empty.");
        }
        if (options.Dimensions <= 0)
        {
            return ValidateOptionsResult.Fail("OllamaOptions.Dimensions must be > 0.");
        }
        if (options.Timeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("OllamaOptions.Timeout must be positive.");
        }
        return ValidateOptionsResult.Success;
    }
}
