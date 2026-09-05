using Xunit;

namespace RiuTek.API.IntegrationTest.Infrastructure;

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
    public const string Name = "PostgreSQL Integration Tests";
}
