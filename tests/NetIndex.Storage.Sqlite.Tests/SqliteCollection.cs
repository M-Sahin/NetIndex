using NetIndex.Storage.Sqlite.Tests.Fixtures;
using NetIndex.Testing.Common;

namespace NetIndex.Storage.Sqlite.Tests;

/// <summary>xUnit collection definition for SQLite vector store tests.</summary>
[CollectionDefinition(TestingConstants.Collections.Sqlite)]
public class SqliteCollection : ICollectionFixture<SqliteFixture>
{
}
