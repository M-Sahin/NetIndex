using NetIndex.Storage.Pgvector.Tests.Fixtures;
using NetIndex.Testing.Common;
using Xunit;

namespace NetIndex.Storage.Pgvector.Tests;

/// <summary>xUnit collection definition for pgvector tests sharing a single <see cref="PostgresFixture"/>.</summary>
[CollectionDefinition(TestingConstants.Collections.Pgvector)]
public sealed class PgvectorCollection : ICollectionFixture<PostgresFixture>
{
}
