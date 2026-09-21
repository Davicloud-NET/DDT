// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Core.Configuration;
using DDT.Server.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using IPNetwork = System.Net.IPNetwork;

namespace DDT.Server.Tests;

public sealed class ForwardedHeadersConfigurationTests
{
    [Fact]
    public void NothingConfiguredForwardsNothing()
    {
        ForwardedHeadersOptions options = Trust(string.Empty, string.Empty);

        Assert.Equal(ForwardedHeaders.None, options.ForwardedHeaders);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }

    [Fact]
    public void OnlyTheListedProxiesAndNetworksAreTrusted()
    {
        ForwardedHeadersOptions options = Trust(" 192.0.2.10 , 2001:db8::10,", "198.51.100.0/24, fd00::/64");

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal([IPAddress.Parse("192.0.2.10"), IPAddress.Parse("2001:db8::10")], options.KnownProxies);
        Assert.Equal([IPNetwork.Parse("198.51.100.0/24"), IPNetwork.Parse("fd00::/64")], options.KnownIPNetworks);
        Assert.Equal(1, options.ForwardLimit);
    }

    [Theory]
    [InlineData("proxy.example.com", "")]
    [InlineData("10.20", "")]
    [InlineData("198.51.100.0/24", "")]
    [InlineData("", "198.51.100.7")]
    [InlineData("", "198.51.100.7/24")]
    [InlineData("", "10.20/16")]
    [InlineData("", "198.51.100.0/33")]
    public void MistakesAreRefused(string proxies, string networks)
    {
        SettingProblem problem = Assert.Single(
            DdtForwardedHeadersExtensions.FindProblems(new DdtForwardedHeadersOptions { KnownProxies = proxies, KnownNetworks = networks }));

        Assert.Equal(proxies.Length > 0 ? "KnownProxies" : "KnownNetworks", problem.Field);
        Assert.StartsWith($"'{proxies}{networks}'", problem.Message, StringComparison.Ordinal);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Trust(proxies, networks));
        Assert.Contains($"DDT:ForwardedHeaders:{problem.Field}: '{proxies}{networks}'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMistakeStopsTheHost()
    {
        using DdtApplication application = new();
        using WebApplicationFactory<Program> misconfigured = application.WithWebHostBuilder(
            builder => builder.UseSetting("DDT:ForwardedHeaders:KnownNetworks", "198.51.100.7/24"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => misconfigured.CreateClient());

        Assert.Contains("DDT:ForwardedHeaders", error.Message, StringComparison.Ordinal);
    }

    private static ForwardedHeadersOptions Trust(string proxies, string networks)
    {
        ForwardedHeadersOptions options = new();
        DdtForwardedHeadersExtensions.TrustOnly(options, new DdtForwardedHeadersOptions { KnownProxies = proxies, KnownNetworks = networks });

        return options;
    }
}
