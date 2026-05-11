namespace NetIndex.Storage.InMemory.Tests;

using NetIndex.Storage.InMemory.Tests.Fixtures;
using NetIndex.Testing.Common;

/// <summary>
/// xUnit collection definition for in-memory vector store tests.
/// </summary>
[CollectionDefinition(TestingConstants.Collections.InMemory)]
public class InMemoryCollection : ICollectionFixture<InMemoryFixture>
{
}