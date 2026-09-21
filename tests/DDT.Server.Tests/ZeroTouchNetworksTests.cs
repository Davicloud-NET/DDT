// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Server.Deployments;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ZeroTouchNetworksTests
{
    private static readonly ZeroTouchNetworks s_networks = ZeroTouchNetworks.Parse(" 10.20.0.0/16, 192.168.7.0/24,fd00:20::/64 ");

    [Theory]
    [InlineData("10.20.1.2", true)]
    [InlineData("192.168.7.200", true)]
    [InlineData("fd00:20::1234", true)]
    [InlineData("::ffff:10.20.1.2", true)]
    [InlineData("10.21.1.2", false)]
    [InlineData("::ffff:10.21.1.2", false)]
    [InlineData("fd00:21::1", false)]
    [InlineData("127.0.0.1", false)]
    public void MatchesTheClientAddressIPv4MappedOrNot(string address, bool listed)
    {
        Assert.Equal(listed, s_networks.Contains(IPAddress.Parse(address)));
    }

    [Fact]
    public void MatchesNothingWithoutAnAddressOrWhenEmpty()
    {
        Assert.False(s_networks.Contains(null));
        Assert.False(ZeroTouchNetworks.Parse(string.Empty).Contains(IPAddress.Parse("10.20.1.2")));
        Assert.False(ZeroTouchNetworks.Parse(null).Contains(IPAddress.Parse("10.20.1.2")));
    }

    [Fact]
    public void RefusesEveryEntryThatIsNotANetwork()
    {
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
            () => ZeroTouchNetworks.Parse("10.20.0.0/16, 10.30.0.1/16, lab, 10.40.0.0/33, 10.50.0.5, fd00:30::1/64"));

        Assert.DoesNotContain("'10.20.0.0/16'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'10.30.0.1/16'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'lab'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'10.40.0.0/33'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'10.50.0.5'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'fd00:30::1/64'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OneMachineIsANetworkOfOne()
    {
        ZeroTouchNetworks single = ZeroTouchNetworks.Parse("10.20.1.5/32");

        Assert.True(single.Contains(IPAddress.Parse("10.20.1.5")));
        Assert.False(single.Contains(IPAddress.Parse("10.20.1.6")));
    }
}
