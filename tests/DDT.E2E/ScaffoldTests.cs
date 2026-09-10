using Xunit;

namespace DDT.E2E;

public sealed class ScaffoldTests
{
    [Fact]
    [Trait("Category", "E2E")]
    public void ProjectIsWiredIntoTheTestRun()
    {
        Assert.True(true);
    }
}
