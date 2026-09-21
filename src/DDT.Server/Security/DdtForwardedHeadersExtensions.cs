// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;

namespace DDT.Server.Security;

public static class DdtForwardedHeadersExtensions
{
    // The unnamed options belong to ASPNETCORE_FORWARDEDHEADERS_ENABLED, which has the host put its own copy of the
    // middleware in front of everything with them.
    internal const string OptionsName = "DDT";

    public static IServiceCollection AddDdtForwardedHeaders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<DdtForwardedHeadersOptions>()
            .BindConfiguration(DdtForwardedHeadersOptions.SectionName, binder => binder.ErrorOnUnknownConfiguration = true);

        services.AddOptions<ForwardedHeadersOptions>(OptionsName)
            .PostConfigure<IOptions<DdtForwardedHeadersOptions>>((options, configured) => TrustOnly(options, configured.Value));

        // The switch's own setup clears both lists, so the host's copy would trust every address and take the entry of
        // X-Forwarded-For the proxy added, leaving the client's to DDT's copy. PostConfigure runs after that setup.
        services.PostConfigure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.None);

        services.AddSingleton<ListedProxies>();

        return services;
    }

    public static IApplicationBuilder UseDdtForwardedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        ForwardedHeadersOptions options = app.ApplicationServices
            .GetRequiredService<IOptionsMonitor<ForwardedHeadersOptions>>()
            .Get(OptionsName);

        // The middleware takes the first entry from a connection with no address, such as a Unix socket or a named
        // pipe, as if a listed proxy had sent it.
        return options.ForwardedHeaders == ForwardedHeaders.None
            ? app
            : app.UseWhen(context => context.Connection.RemoteIpAddress is not null, proxied => proxied.UseForwardedHeaders(options));
    }

    // Replaces the framework defaults rather than adding to them, because those trust loopback: a proxy on the same
    // host is listed like any other.
    public static void TrustOnly(ForwardedHeadersOptions options, DdtForwardedHeadersOptions configured)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configured);

        List<string> failures = [];
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (string proxy in Split(configured.KnownProxies))
        {
            if (TryParseAddress(proxy) is { } address)
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                failures.Add($"DDT:ForwardedHeaders:KnownProxies contains '{proxy}', which is not an IP address.");
            }
        }

        foreach (string network in Split(configured.KnownNetworks))
        {
            if (TryParseNetwork(network) is { } parsed)
            {
                options.KnownIPNetworks.Add(parsed);
            }
            else
            {
                failures.Add(
                    $"DDT:ForwardedHeaders:KnownNetworks contains '{network}', which is not a network such as 10.20.0.0/24 " +
                    "with no address bits set past the prefix length.");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "DDT:ForwardedHeaders is not valid:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        bool trustsAny = options.KnownProxies.Count > 0 || options.KnownIPNetworks.Count > 0;

        options.ForwardedHeaders = trustsAny
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            : ForwardedHeaders.None;

        // Only the entry the nearest proxy added. Anything to its left came from the client.
        options.ForwardLimit = 1;
    }

    private static string[] Split(string configured) =>
        configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // IPAddress.TryParse reads "10.20" as 10.0.0.20, so an IPv4 address has to be written out in full.
    private static IPAddress? TryParseAddress(string value) =>
        IPAddress.TryParse(value, out IPAddress? address)
        && (address.AddressFamily != AddressFamily.InterNetwork || string.Equals(address.ToString(), value, StringComparison.Ordinal))
            ? address
            : null;

    // IPNetwork.TryParse silently clears host bits, which would turn a mistyped single proxy into a whole subnet.
    private static IPNetwork? TryParseNetwork(string value)
    {
        int slash = value.IndexOf('/', StringComparison.Ordinal);

        return slash > 0
            && TryParseAddress(value[..slash]) is { } address
            && IPNetwork.TryParse(value, out IPNetwork network)
            && network.BaseAddress.Equals(address)
                ? network
                : null;
    }
}
