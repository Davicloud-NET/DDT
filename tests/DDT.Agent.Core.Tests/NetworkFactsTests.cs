// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Facts;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class NetworkFactsTests
{
    private static NetworkFacts From(
        (string Address, int PrefixLength)[]? unicast = null,
        string[]? gateways = null,
        string? dnsSuffix = null,
        string[]? dhcpServers = null) =>
        NetworkFacts.From(
            (unicast ?? []).Select(entry => (IPAddress.Parse(entry.Address), entry.PrefixLength)),
            (gateways ?? []).Select(IPAddress.Parse),
            dnsSuffix,
            (dhcpServers ?? []).Select(IPAddress.Parse));

    [Fact]
    public void ReadsTheAdaptersSettings()
    {
        NetworkFacts facts = From([("10.1.2.3", 24)], ["10.1.2.1"], "corp.example.com", ["10.1.0.5"]);

        Assert.Equal(new NetworkFacts("10.1.2.3", 24, "10.1.2.1", "corp.example.com", "10.1.0.5"), facts);
    }

    // Windows gives an adapter a link-local address while no DHCP server answers, and may keep it beside the leased one.
    [Fact]
    public void TakesTheFirstIPv4AddressThatIsNotLinkLocal()
    {
        NetworkFacts facts = From([("fe80::1", 64), ("169.254.10.20", 16), ("10.1.2.3", 24), ("10.1.2.4", 24)]);

        Assert.Equal(("10.1.2.3", (int?)24), (facts.IPv4Address, facts.PrefixLength));
    }

    [Fact]
    public void TakesALinkLocalAddressWhenThereIsNoOther()
    {
        NetworkFacts facts = From([("169.254.10.20", 16)]);

        Assert.Equal(("169.254.10.20", (int?)16), (facts.IPv4Address, facts.PrefixLength));
    }

    [Fact]
    public void SkipsGatewaysAndDhcpServersThatAreUnsetOrNotIPv4()
    {
        NetworkFacts facts = From(gateways: ["0.0.0.0", "fe80::1", "10.1.2.1"], dhcpServers: ["255.255.255.255", "10.1.0.5"]);

        Assert.Equal(("10.1.2.1", "10.1.0.5"), (facts.DefaultGateway, facts.DhcpServer));
    }

    [Fact]
    public void TrimsTheDnsSuffixAndLeavesAnEmptyOneUnknown()
    {
        Assert.Equal("corp.example.com", From(dnsSuffix: " corp.example.com ").DnsSuffix);
        Assert.Null(From(dnsSuffix: "").DnsSuffix);
    }

    [Fact]
    public void LeavesEverythingUnknownForAnAdapterWithoutSettings()
    {
        Assert.Equal(new NetworkFacts(null, null, null, null, null), From(unicast: [("::1", 128)], gateways: ["::"], dhcpServers: ["0.0.0.0"]));
    }
}
