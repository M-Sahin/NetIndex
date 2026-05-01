using Microsoft.Extensions.Options;

namespace NetIndex.Core.Options;

/// <summary>
/// Validates baseline NetIndex options at build time.
/// </summary>
public sealed class NetIndexOptionsValidator : IValidateOptions<NetIndexOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, NetIndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ValidateOptionsResult.Success;
    }
}
