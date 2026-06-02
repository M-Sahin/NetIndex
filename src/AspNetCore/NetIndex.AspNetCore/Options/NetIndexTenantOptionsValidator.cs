using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace NetIndex.AspNetCore.Options;

/// <summary>
/// Validates <see cref="NetIndexTenantOptions"/> on application startup.
/// </summary>
internal sealed partial class NetIndexTenantOptionsValidator : IValidateOptions<NetIndexTenantOptions>
{
    // RFC 7230 token grammar: one or more tchar characters.
    // tchar = "!" / "#" / "$" / "%" / "&" / "'" / "*" / "+" / "-" / "." /
    //         "^" / "_" / "`" / "|" / "~" / DIGIT / ALPHA
    [GeneratedRegex(@"^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeaderTokenRegex();

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, NetIndexTenantOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HeaderName))
        {
            return ValidateOptionsResult.Fail("NetIndexTenantOptions.HeaderName must not be blank.");
        }

        if (!HeaderTokenRegex().IsMatch(options.HeaderName))
        {
            return ValidateOptionsResult.Fail(
                $"NetIndexTenantOptions.HeaderName '{options.HeaderName}' is not a valid RFC 7230 header token. " +
                "Values must match ^[!#$%&'*+\\-.^_`|~0-9A-Za-z]+$. " +
                "Remove padding whitespace, spaces, and control characters.");
        }

        // A pure-whitespace ClaimsHeaderPrefix is treated as disabled (no validation needed).
        // Only validate the grammar when a non-whitespace prefix is provided.
        if (!string.IsNullOrWhiteSpace(options.ClaimsHeaderPrefix) &&
            !HeaderTokenRegex().IsMatch(options.ClaimsHeaderPrefix))
        {
            return ValidateOptionsResult.Fail(
                $"NetIndexTenantOptions.ClaimsHeaderPrefix '{options.ClaimsHeaderPrefix}' is not a valid RFC 7230 header token prefix. " +
                "Values must match ^[!#$%&'*+\\-.^_`|~0-9A-Za-z]+$. " +
                "Remove padding whitespace, spaces, and control characters.");
        }

        return ValidateOptionsResult.Success;
    }
}
