using System.Threading.Channels;
using FluentAssertions;
using NetIndex.AspNetCore.Options;
using Xunit;

namespace NetIndex.AspNetCore.Tests.BackgroundServices;

/// <summary>Unit tests for <see cref="BackgroundIngestionOptionsValidator"/>.</summary>
public class BackgroundIngestionOptionsValidatorTests
{
    /// <summary>Non-positive queue capacities are rejected.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void BackgroundIngestionOptionsValidator_RejectsNonPositiveCapacity(int capacity)
    {
        var validator = new BackgroundIngestionOptionsValidator();
        var options = new BackgroundIngestionOptions { QueueCapacity = capacity };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("QueueCapacity");
    }

    /// <summary>Positive queue capacities are accepted.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void BackgroundIngestionOptionsValidator_AcceptsPositiveCapacity(int capacity)
    {
        var validator = new BackgroundIngestionOptionsValidator();
        var options = new BackgroundIngestionOptions { QueueCapacity = capacity };

        var result = validator.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    /// <summary>Undefined FullMode enum values are rejected to prevent ArgumentOutOfRangeException at channel creation.</summary>
    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void BackgroundIngestionOptionsValidator_RejectsUndefinedFullMode(int modeValue)
    {
        var validator = new BackgroundIngestionOptionsValidator();
        var options = new BackgroundIngestionOptions { FullMode = (BoundedChannelFullMode)modeValue };

        var result = validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("FullMode");
    }
}
