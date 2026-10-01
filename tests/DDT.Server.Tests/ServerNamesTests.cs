// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Server.Certificates;
using Xunit;

namespace DDT.Server.Tests;

public sealed class ServerNamesTests
{
    [Theory]
    [InlineData("deploy01", "contoso.local", "deploy01.contoso.local")]
    [InlineData("deploy01", "contoso.local.", "deploy01.contoso.local")]
    [InlineData("deploy01", "", "deploy01")]
    // What Linux reports without a domain
    [InlineData("3f2a9c1d7b44", "(none)", "3f2a9c1d7b44")]
    // A host name that is the DNS name already
    [InlineData("deploy01.contoso.local", "contoso.local", "deploy01.contoso.local")]
    public void TheDnsNameIsTheHostWithItsDomainWhereThatMakesAName(string host, string domain, string expected) =>
        Assert.Equal(expected, ServerNames.DnsName(host, domain));

    [Fact]
    public void TheRequiredNamesHoldTheDnsNameOnceAndTheConfiguredOnes()
    {
        IReadOnlyList<string> names = ServerNames.Required($"ddt.example, {Dns.GetHostName()}");

        Assert.Equal(["localhost", Dns.GetHostName()], names.Take(2));
        Assert.Contains(ServerNames.DnsName(), names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ddt.example", names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TheAddressesStartWithLoopbackAndNameEachOnce()
    {
        IReadOnlyList<IPAddress> addresses = ServerNames.LocalAddresses();

        Assert.Equal([IPAddress.Loopback, IPAddress.IPv6Loopback], addresses.Take(2));
        Assert.Equal(addresses.Count, addresses.Distinct().Count());
    }
}
