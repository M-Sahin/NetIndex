using Microsoft.Extensions.Options;

namespace NetIndex.Providers.TextEmbeddingsInference.Options;

/// <summary>Validates <see cref="TeiRerankerOptions"/> at build time.</summary>
public sealed class TeiRerankerOptionsValidator : IValidateOptions<TeiRerankerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TeiRerankerOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            return ValidateOptionsResult.Fail("TeiRerankerOptions.Endpoint must not be empty.");
        }
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return ValidateOptionsResult.Fail("TeiRerankerOptions.Endpoint must be a valid absolute http(s) URI.");
        }
        if (options.ConnectTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("TeiRerankerOptions.ConnectTimeout must be positive.");
        }
        if (options.RequestTimeout <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("TeiRerankerOptions.RequestTimeout must be positive.");
        }
        if (options.FailureThreshold < 1)
        {
            return ValidateOptionsResult.Fail("TeiRerankerOptions.FailureThreshold must be >= 1.");
        }
        if (options.BreakDuration <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("TeiRerankerOptions.BreakDuration must be positive.");
        }
        return ValidateOptionsResult.Success;
    }
}
