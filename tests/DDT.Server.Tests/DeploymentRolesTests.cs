using DDT.Server.Configuration;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DeploymentRolesTests
{
    [Fact]
    public void EmptyConfigurationDefaultsToWeb()
    {
        Assert.Equal(DeploymentRoles.Default, DeploymentRoles.Parse(""));
        Assert.Equal(DeploymentRoles.Default, DeploymentRoles.Parse(null));
        Assert.Equal(DeploymentRoles.Default, DeploymentRoles.Parse("   "));
    }

    [Theory]
    [InlineData("web", DeploymentRole.Web)]
    [InlineData("PXE", DeploymentRole.Pxe)]
    [InlineData("  builder  ", DeploymentRole.Builder)]
    public void SingleRoleIsParsedCaseInsensitively(string configured, DeploymentRole expected)
    {
        Assert.Equal([expected], DeploymentRoles.Parse(configured));
    }

    [Fact]
    public void MultipleRolesAreParsed()
    {
        IReadOnlySet<DeploymentRole> roles = DeploymentRoles.Parse("web,pxe");

        Assert.Equal(2, roles.Count);
        Assert.Contains(DeploymentRole.Web, roles);
        Assert.Contains(DeploymentRole.Pxe, roles);
    }

    [Fact]
    public void RepeatedRolesCollapse()
    {
        Assert.Equal([DeploymentRole.Web], DeploymentRoles.Parse("web,web"));
    }

    [Fact]
    public void UnknownRoleThrows()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DeploymentRoles.Parse("web,wbe"));

        Assert.Contains("wbe", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericRoleIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => DeploymentRoles.Parse("1"));
    }
}
