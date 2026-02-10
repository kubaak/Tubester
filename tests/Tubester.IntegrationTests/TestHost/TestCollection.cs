using Xunit;

namespace Tubester.IntegrationTests.TestHost;

[CollectionDefinition(nameof(TestCollection))]
public class TestCollection : ICollectionFixture<TestFixture>
{
}