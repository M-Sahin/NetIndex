using Microsoft.Extensions.Options;

namespace NetIndex.Storage.Pgvector.Options;

internal sealed class PgvectorOptionsValidator : IValidateOptions<PgvectorOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PgvectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("PgvectorOptions.ConnectionString is required and must not be blank.");
        }

        if (options.Dimensions <= 0)
        {
            failures.Add("PgvectorOptions.Dimensions must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
