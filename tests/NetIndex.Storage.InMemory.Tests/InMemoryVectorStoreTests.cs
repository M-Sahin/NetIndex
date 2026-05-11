namespace NetIndex.Storage.InMemory.Tests;

using NetIndex.Core.Abstractions;
using NetIndex.Storage.InMemory.Tests.Fixtures;
using NetIndex.Testing.Common;

/// <summary>
/// Runs the full VectorStoreContractSuite against InMemoryVectorStore.
/// </summary>
[Collection(TestingConstants.Collections.InMemory)]
[Trait("Category", "ContractTest")]
public class InMemoryVectorStoreTests : VectorStoreContractSuite
{
    private readonly InMemoryFixture _fixture;

    /// <summary>
    /// Initializes with the shared fixture.
    /// </summary>
    public InMemoryVectorStoreTests(InMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override IVectorStore Store => _fixture.Store;

    /// <inheritdoc />
    public override Task InitializeAsync() => _fixture.ResetAsync();

    /// <inheritdoc />
    public override Task DisposeAsync() => Task.CompletedTask;
}