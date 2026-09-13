using System.Net;
using Xunit;

namespace DDT.Pxe.Tests;

public sealed class NetworkInterfaceMapTests
{
    private static readonly ServedInterface[] s_candidates =
    [
        new(20, "Ethernet", IPAddress.Parse("10.0.4.144")),
        new(25, "vEthernet (Default Switch)", IPAddress.Parse("172.24.64.1")),
    ];

    [Fact]
    public void ServesNothingWhenNothingIsConfigured()
    {
        NetworkInterfaceMap map = new(string.Empty, s_candidates);

        Assert.Empty(map.Served);
        Assert.False(map.TryGetInterface(20, out _));
        Assert.Equal(2, map.Candidates.Count);
    }

    [Fact]
    public void MatchesByNameIgnoringCaseOrByAddress()
    {
        NetworkInterfaceMap map = new("vethernet (default switch), 10.0.4.144", s_candidates);

        Assert.Equal(2, map.Served.Count);
        Assert.True(map.TryGetInterface(25, out ServedInterface? served));
        Assert.Equal(IPAddress.Parse("172.24.64.1"), served.Address);
        Assert.Empty(map.Unmatched);
    }

    [Fact]
    public void ReportsConfiguredNamesThatMatchNoInterface()
    {
        NetworkInterfaceMap map = new("Ethernet, wg0", s_candidates);

        Assert.Single(map.Served);
        Assert.Equal(["wg0"], map.Unmatched);
    }

    [Fact]
    public void MatchesAndServesASecondaryAddress()
    {
        ServedInterface lan = new(20, "Ethernet", [IPAddress.Parse("10.0.4.144"), IPAddress.Parse("10.0.4.145")]);
        NetworkInterfaceMap map = new("10.0.4.145", [lan]);

        Assert.True(map.TryGetInterface(20, out ServedInterface? served));
        Assert.Equal(IPAddress.Parse("10.0.4.144"), served.Address);
        Assert.True(map.IsServedAddress(IPAddress.Parse("10.0.4.145")));
    }

    [Fact]
    public void RecognisesServedAddressesIncludingMappedForms()
    {
        NetworkInterfaceMap map = new("Ethernet", s_candidates);

        Assert.True(map.IsServedAddress(IPAddress.Parse("10.0.4.144")));
        Assert.True(map.IsServedAddress(IPAddress.Parse("10.0.4.144").MapToIPv6()));
        Assert.False(map.IsServedAddress(IPAddress.Parse("172.24.64.1")));
        Assert.False(map.IsServedAddress(IPAddress.Broadcast));
    }
}
