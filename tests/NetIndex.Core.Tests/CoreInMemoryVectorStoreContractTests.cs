#pragma warning disable CS1591
using NetIndex.Core;
using NetIndex.Core.Abstractions;
using NetIndex.Testing.Common;
using Xunit;

namespace NetIndex.Core.Tests;

/// <summary>
/// Runs the shared <see cref="VectorStoreContractSuite"/> against the zero-config Core
/// <see cref="InMemoryVectorStore"/> so its tenant-scoped delete is covered like the other built-in stores.
/// </summary>
[Trait("Category", "ContractTest")]
public sealed class CoreInMemoryVectorStoreContractTests : VectorStoreContractSuite
{
    private readonly InMemoryVectorStore _store = new();

    protected override IVectorStore Store => _store;

    protected override Type DimensionMismatchExceptionType => typeof(NetIndexStorageException);
}
