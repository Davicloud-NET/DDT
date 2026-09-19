using DDT.Core.Unattend;
using Xunit;

namespace DDT.Core.Tests;

public sealed class WindowsTimeZonesTests
{
    [Theory]
    [InlineData("W. Europe Standard Time")]
    [InlineData("GMT Standard Time")]
    [InlineData("Pacific Standard Time")]
    [InlineData("UTC")]
    public void AcceptsAWindowsId(string id)
    {
        Assert.True(WindowsTimeZones.IsValidId(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Europe/Berlin")]
    [InlineData("Etc/UTC")]
    [InlineData("Mars Standard Time")]
    [InlineData("W. Europe Standard Time ")]
    public void RefusesAnythingElse(string id)
    {
        Assert.False(WindowsTimeZones.IsValidId(id));
    }

    [Fact]
    public void RefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => WindowsTimeZones.IsValidId(null!));
    }
}
