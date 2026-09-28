// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Pxe;
using DDT.Server.Configuration;

namespace DDT.Host.Startup;

// Every authentication control depends on TLS: over plain HTTP Secure cookies are dropped, so a sign-in seems to work and
// every later request is anonymous. Reads configuration, because Kestrel:Endpoints does not fill IServerAddressesFeature.
public static class HttpsConfigurationCheck
{
    public static void Validate(IConfiguration configuration, DdtOptions options, IReadOnlySet<DeploymentRole> roles)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(roles);

        IConfigurationSection[] endpoints = [.. configuration.GetSection("Kestrel:Endpoints").GetChildren()];

        // Kestrel ignores every URL and port setting once any endpoint is declared. The pxe role declares
        // one, so without another the UI and API would silently stop listening, behind a proxy too.
        if (endpoints.Length > 0 && endpoints.All(endpoint => endpoint.Key == PxeHostingExtensions.BootEndpointName))
        {
            throw new InvalidOperationException(
                "The pxe role adds Kestrel:Endpoints:Boot, so Kestrel ignores ASPNETCORE_URLS, ASPNETCORE_HTTP_PORTS and " +
                "launch profile URLs. Declare the application endpoint as Kestrel:Endpoints:Https:Url, or as an http " +
                "Kestrel endpoint when DDT:RequireHttps is false.");
        }

        // Netbooted machines reach the web role over this same endpoint. Bound to loopback it answers this
        // computer alone, and every agent times out with nothing on either side saying why.
        string[] httpsUrls = [.. endpoints.Select(endpoint => endpoint["Url"]).Where(IsHttps).OfType<string>()];

        if (roles.Contains(DeploymentRole.Pxe) && roles.Contains(DeploymentRole.Web) && httpsUrls.Length > 0 && httpsUrls.All(IsLoopback))
        {
            throw new InvalidOperationException(
                $"The pxe role serves machines that register over HTTPS, but {string.Join(", ", httpsUrls)} only answers " +
                "this computer. Listen on the provisioning network instead, for example Kestrel:Endpoints:Https:Url=https://0.0.0.0:7152.");
        }

        if (!options.RequireHttps || HasHttpsEndpoint(configuration, endpoints))
        {
            return;
        }

        throw new InvalidOperationException(
            "DDT:RequireHttps is set but no HTTPS endpoint is configured. Set Kestrel:Endpoints:Https:Url to an " +
            "https URL, or set DDT:RequireHttps to false when a reverse proxy terminates TLS and list the proxy in " +
            "DDT:ForwardedHeaders:KnownProxies.");
    }

    private static bool HasHttpsEndpoint(IConfiguration configuration, IConfigurationSection[] endpoints)
    {
        if (endpoints.Length > 0)
        {
            return endpoints.Any(endpoint => IsHttps(endpoint["Url"]));
        }

        if (!string.IsNullOrWhiteSpace(configuration["ASPNETCORE_HTTPS_PORTS"]))
        {
            return true;
        }

        string? urls = configuration["Urls"] ?? configuration["ASPNETCORE_URLS"];

        return urls is not null
            && urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(IsHttps);
    }

    private static bool IsHttps(string? url) =>
        url is not null && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    // Wildcards such as https://*:7152 are not valid URIs and listen everywhere, so they are not loopback.
    private static bool IsLoopback(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.IsLoopback || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? address) && IPAddress.IsLoopback(address)));
}
