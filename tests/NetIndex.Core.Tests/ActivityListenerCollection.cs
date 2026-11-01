#pragma warning disable CS1591
using Xunit;

namespace NetIndex.Core.Tests;

/// <summary>
/// Test classes that register a process-wide <see cref="System.Diagnostics.ActivityListener"/> on the
/// "NetIndex" source run in this collection, so they never run in parallel with each other.
/// <c>PipelineTracingTests.NoListener_*</c> asserts that the source has no listener at all, which another
/// class's listener would break (Story 2.14).
/// </summary>
[CollectionDefinition(NAME, DisableParallelization = true)]
public sealed class ActivityListenerCollection
{
    public const string NAME = "NetIndex activity listeners";
}
