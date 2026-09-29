// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using DDT.Contracts.Messages;
using DDT.Core.Configuration;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IPNetwork = System.Net.IPNetwork;

namespace DDT.Server.Security;

public static class DdtForwardedHeadersExtensions
{
    public static IServiceCollection AddDdtForwardedHeaders(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ASPNETCORE_FORWARDEDHEADERS_ENABLED makes the host add its own copy of the middleware first. That copy uses
        // the unnamed options, and their setup clears both lists. So it would trust every address and take the proxy's
        // X-Forwarded-For entry, leaving only the client's entry for DDT's copy. None turns the host's copy off, and
        // PostConfigure runs after that setup.
        services.PostConfigure<ForwardedHeadersOptions>(options => options.ForwardedHeaders = ForwardedHeaders.None);

        services.AddSingleton<ListedProxies>();

        return services;
    }

    // The proxies section can change while the server runs. So the framework's middleware is rebuilt for each settings
    // snapshot instead of living for the whole process, and DDT only calls its ApplyForwarders.
    public static IApplicationBuilder UseDdtForwardedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        DdtSettings settings = app.ApplicationServices.GetRequiredService<DdtSettings>();
        Forwarders forwarders = new(app.ApplicationServices.GetRequiredService<ILoggerFactory>());

        return app.Use((context, next) =>
        {
            ForwardedHeadersOptions options = settings.Current.ForwardedHeaders;

            // On a connection with no address, such as a Unix socket or a named pipe, the middleware would take the
            // first entry as if a listed proxy had sent it.
            if (options.ForwardedHeaders != ForwardedHeaders.None && context.Connection.RemoteIpAddress is not null)
            {
                forwarders.For(options).ApplyForwarders(context);
            }

            return next(context);
        });
    }

    public static IReadOnlyList<SettingProblem> FindProblems(DdtForwardedHeadersOptions configured)
    {
        ArgumentNullException.ThrowIfNull(configured);

        return
        [
            .. Split(configured.KnownProxies)
                .Where(proxy => TryParseAddress(proxy) is null)
                .Select(proxy => new SettingProblem("KnownProxies", ServerMessages.SettingsProxiesAddressInvalid.With("value", proxy))),
            .. Split(configured.KnownNetworks)
                .Where(network => TryParseNetwork(network) is null)
                .Select(network => new SettingProblem("KnownNetworks", ServerMessages.SettingsProxiesNetworkInvalid.With("value", network))),
            .. Split(configured.KnownNetworks)
                .Where(network => TryParseNetwork(network) is { PrefixLength: 0 })
                .Select(network => new SettingProblem("KnownNetworks", ServerMessages.SettingsProxiesNetworkEverything.With("value", network))),
        ];
    }

    // Replaces the framework defaults instead of adding to them, because the defaults trust loopback. A proxy on the
    // same host has to be listed like any other.
    public static void TrustOnly(ForwardedHeadersOptions options, DdtForwardedHeadersOptions configured)
    {
        ArgumentNullException.ThrowIfNull(options);
        SettingProblem.ThrowIfAny(DdtForwardedHeadersOptions.SectionName, FindProblems(configured));

        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (string proxy in Split(configured.KnownProxies))
        {
            options.KnownProxies.Add(TryParseAddress(proxy)!);
        }

        foreach (string network in Split(configured.KnownNetworks))
        {
            options.KnownIPNetworks.Add(TryParseNetwork(network)!.Value);
        }

        bool trustsAny = options.KnownProxies.Count > 0 || options.KnownIPNetworks.Count > 0;

        options.ForwardedHeaders = trustsAny
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            : ForwardedHeaders.None;

        // Only use the entry the nearest proxy added. Anything to its left came from the client.
        options.ForwardLimit = 1;
    }

    // Returns the entries that parse as addresses and networks, for the rules that compare them with other networks.
    public static IReadOnlyList<IPAddress> Proxies(string configured) => [.. Split(configured).Select(TryParseAddress).OfType<IPAddress>()];

    public static IReadOnlyList<IPNetwork> Networks(string configured) => [.. Split(configured).Select(TryParseNetwork).OfType<IPNetwork>()];

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

    // Keeps the middleware for the last snapshot and only rebuilds it when the proxies changed.
    private sealed class Forwarders(ILoggerFactory loggerFactory)
    {
        private Built? _built;

        public ForwardedHeadersMiddleware For(ForwardedHeadersOptions options)
        {
            Built? built = Volatile.Read(ref _built);

            if (built is null || !ReferenceEquals(built.Options, options))
            {
                built = new Built(options, new ForwardedHeadersMiddleware(_ => Task.CompletedTask, loggerFactory, Options.Create(options)));
                Volatile.Write(ref _built, built);
            }

            return built.Middleware;
        }

        private sealed record Built(ForwardedHeadersOptions Options, ForwardedHeadersMiddleware Middleware);
    }
}
