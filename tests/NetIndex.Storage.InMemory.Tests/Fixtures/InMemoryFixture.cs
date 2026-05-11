namespace NetIndex.Storage.InMemory.Tests.Fixtures;

using Microsoft.Extensions.Options;
using NetIndex.Storage.InMemory;
using NetIndex.Storage.InMemory.Options;
using NetIndex.Testing.Common;

/// <summary>
/// xUnit fixture providing a fresh InMemoryVectorStore for each test.
/// Reset is synchronous — just replaces the store reference (no connection to close).
/// </summary>
public sealed class InMemoryFixture : IAsyncLifetime, IResetable
{
    /// <summary>Dimensions used for all test vectors — kept small for fast test execution.</summary>
    public const int TestDimensions = 4;
    private InMemoryVectorStore? _store;

    /// <summary>
    /// Gets the current InMemoryVectorStore under test.
    /// </summary>
    public InMemoryVectorStore Store => _store!;

    /// <inheritdoc/>
    public Task InitializeAsync()
    {
        _store = CreateStore();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ResetAsync()
    {
        _store = CreateStore();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    private static InMemoryVectorStore CreateStore()
    {
        var options = new InMemoryOptions { Dimensions = TestDimensions };
        return new InMemoryVectorStore(new OptionsWrapper<InMemoryOptions>(options));
    }
}