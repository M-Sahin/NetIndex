using Microsoft.Extensions.Options;

namespace NetIndex.AspNetCore.Options;

/// <summary>
/// Validates <see cref="NetIndexTenantOptions"/> on application startup.
/// </summary>
internal sealed class NetIndexTenantOptionsValidator : IValidateOptions<NetIndexTenantOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, NetIndexTenantOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HeaderName))
        {
            return ValidateOptionsResult.Fail("NetIndexTenantOptions.HeaderName must not be blank.");
        }

        return ValidateOptionsResult.Success;
    }
}
