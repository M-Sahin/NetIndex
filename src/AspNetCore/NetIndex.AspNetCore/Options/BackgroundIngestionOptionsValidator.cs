using Microsoft.Extensions.Options;

namespace NetIndex.AspNetCore.Options;

/// <summary>
/// Validates <see cref="BackgroundIngestionOptions"/> at startup via <c>ValidateOnStart</c>.
/// </summary>
internal sealed class BackgroundIngestionOptionsValidator : IValidateOptions<BackgroundIngestionOptions>
{
    public ValidateOptionsResult Validate(string? name, BackgroundIngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.QueueCapacity <= 0)
        {
            return ValidateOptionsResult.Fail(
                "BackgroundIngestionOptions.QueueCapacity must be greater than zero.");
        }

        if (!Enum.IsDefined(options.FullMode))
        {
            return ValidateOptionsResult.Fail(
                $"BackgroundIngestionOptions.FullMode value {(int)options.FullMode} is not a valid BoundedChannelFullMode.");
        }

        return ValidateOptionsResult.Success;
    }
}
