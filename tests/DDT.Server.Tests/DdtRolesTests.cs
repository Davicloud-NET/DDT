using DDT.Server.Configuration;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DdtRolesTests
{
    [Fact]
    public void EmptyConfigurationDefaultsToWeb()
    {
        Assert.Equal(DdtRoles.Default, DdtRoles.Parse(""));
        Assert.Equal(DdtRoles.Default, DdtRoles.Parse(null));
        Assert.Equal(DdtRoles.Default, DdtRoles.Parse("   "));
    }

    [Theory]
    [InlineData("web", DdtRole.Web)]
    [InlineData("PXE", DdtRole.Pxe)]
    [InlineData("  builder  ", DdtRole.Builder)]
    public void SingleRoleIsParsedCaseInsensitively(string configured, DdtRole expected)
    {
        Assert.Equal([expected], DdtRoles.Parse(configured));
    }

    [Fact]
    public void MultipleRolesAreParsed()
    {
        IReadOnlySet<DdtRole> roles = DdtRoles.Parse("web,pxe");

        Assert.Equal(2, roles.Count);
        Assert.Contains(DdtRole.Web, roles);
        Assert.Contains(DdtRole.Pxe, roles);
    }

    [Fact]
    public void RepeatedRolesCollapse()
    {
        Assert.Equal([DdtRole.Web], DdtRoles.Parse("web,web"));
    }

    [Fact]
    public void UnknownRoleThrows()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DdtRoles.Parse("web,wbe"));

        Assert.Contains("wbe", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericRoleIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => DdtRoles.Parse("1"));
    }
}
