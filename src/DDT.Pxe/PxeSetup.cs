// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using DDT.Protocols.Tftp;

namespace DDT.Pxe;

// The validated form of DDT:Pxe. Every configuration mistake that would otherwise produce a machine
// sitting silently at a blank screen is refused here, by name, before a socket is bound.
public sealed class PxeSetup
{
    private const int MaxServerHostNameLength = 63;
    private const int MaxBootFileLength = 255;
    private const int MaxWindowSize = 64;

    private PxeSetup(
        PxeOptions options,
        NetworkInterfaceMap interfaces,
        BootFileResolver files,
        ProxyDhcpConfiguration proxyDhcp,
        TftpLimits tftpLimits)
    {
        Options = options;
        Interfaces = interfaces;
        Files = files;
        ProxyDhcp = proxyDhcp;
        TftpLimits = tftpLimits;
    }

    public PxeOptions Options { get; }

    public NetworkInterfaceMap Interfaces { get; }

    public BootFileResolver Files { get; }

    public ProxyDhcpConfiguration ProxyDhcp { get; }

    public TftpLimits TftpLimits { get; }

    public static PxeSetup Create(PxeOptions options, NetworkInterfaceMap interfaces)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(interfaces);

        List<string> failures = [];
        Dictionary<ClientArchitecture, BootTarget> targets = [];

        foreach ((string key, BootTargetOptions target) in options.BootTargets)
        {
            if (TryReadTarget(key, target, failures) is { } parsed)
            {
                targets[parsed.Architecture] = parsed;
            }
        }

        ImmutableArray<IPAddress>.Builder relays = ImmutableArray.CreateBuilder<IPAddress>();

        foreach (string relay in options.AuthorisedRelayAgents.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseVersion4(relay) is { } address)
            {
                relays.Add(address);
            }
            else
            {
                failures.Add($"DDT:Pxe:AuthorisedRelayAgents contains '{relay}', which is not an IPv4 address.");
            }
        }

        // With ProxyDHCP answering and TFTP off, a Tftp target without its own server would send every
        // client to a TFTP server that is not listening.
        if (options.EnableProxyDhcp && !options.EnableTftp)
        {
            foreach ((ClientArchitecture architecture, BootTarget target) in targets)
            {
                if (target.Method == BootMethod.Tftp && target.ServerAddress is null)
                {
                    failures.Add($"DDT:Pxe:BootTargets:{architecture} uses Tftp while DDT:Pxe:EnableTftp is false. Set its ServerAddress to the TFTP server that serves it.");
                }
            }
        }

        if (options.HttpBootPort is < 1 or > 65535)
        {
            failures.Add($"DDT:Pxe:HttpBootPort {options.HttpBootPort} is not a port number.");
        }

        if (options.TftpMaxWindowSize is < 1 or > MaxWindowSize)
        {
            failures.Add($"DDT:Pxe:TftpMaxWindowSize must be between 1 and {MaxWindowSize}.");
        }

        if (options.MaxConcurrentTftpTransfers < 1)
        {
            failures.Add("DDT:Pxe:MaxConcurrentTftpTransfers must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(options.BootDirectory))
        {
            failures.Add("DDT:Pxe:BootDirectory must be set.");
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "DDT:Pxe is not valid:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }

        ProxyDhcpConfiguration proxyDhcp = new()
        {
            BootTargets = targets.ToFrozenDictionary(),
            LocalAddresses = [.. interfaces.Served.SelectMany(served => served.Addresses)],
            AuthorisedRelayAgents = relays.ToImmutable(),
        };

        return new PxeSetup(
            options,
            interfaces,
            new BootFileResolver(options.BootDirectory),
            proxyDhcp,
            TftpLimits.Default with { MaxWindowSize = options.TftpMaxWindowSize });
    }

    private static BootTarget? TryReadTarget(string key, BootTargetOptions target, List<string> failures)
    {
        string prefix = "DDT:Pxe:BootTargets:" + key;

        // Matched against the member names rather than Enum.TryParse, which also accepts "7".
        string? name = Enum.GetNames<ClientArchitecture>()
            .FirstOrDefault(candidate => candidate.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (name is null)
        {
            failures.Add($"{prefix} is not a client architecture. Use one of: {string.Join(", ", Enum.GetNames<ClientArchitecture>())}.");

            return null;
        }

        ClientArchitecture architecture = Enum.Parse<ClientArchitecture>(name);
        int failuresBefore = failures.Count;

        BootMethod? method = target.Method?.Trim().ToUpperInvariant() switch
        {
            "TFTP" => BootMethod.Tftp,
            "HTTP" => BootMethod.Http,
            _ => null,
        };

        if (method is null)
        {
            failures.Add($"{prefix}:Method must be Tftp or Http.");
        }

        // The architecture already says which one the firmware speaks: a PXE client never accepts a
        // URL and an HTTP Boot client never accepts a TFTP path.
        bool httpArchitecture = name.EndsWith("Http", StringComparison.Ordinal);

        if (method is { } chosen && httpArchitecture != (chosen == BootMethod.Http))
        {
            failures.Add($"{prefix}:Method must be {(httpArchitecture ? "Http" : "Tftp")} for {name} clients.");
        }

        string bootFile = target.BootFile?.Trim() ?? string.Empty;

        if (bootFile.Length == 0)
        {
            failures.Add($"{prefix}:BootFile must be set.");
        }
        else if (bootFile.Length > MaxBootFileLength || !Ascii.IsValid(bootFile))
        {
            failures.Add($"{prefix}:BootFile must be ASCII and at most {MaxBootFileLength} characters.");
        }
        else if (method == BootMethod.Http && !IsHttpUrl(bootFile))
        {
            failures.Add($"{prefix}:BootFile must be an absolute http or https URL.");
        }

        IPAddress? serverAddress = null;

        if (!string.IsNullOrWhiteSpace(target.ServerAddress))
        {
            serverAddress = TryParseVersion4(target.ServerAddress.Trim());

            if (serverAddress is null)
            {
                failures.Add($"{prefix}:ServerAddress '{target.ServerAddress}' is not an IPv4 address.");
            }
        }

        if (target.ServerHostName is { } hostName
            && (hostName.Length > MaxServerHostNameLength || !Ascii.IsValid(hostName)))
        {
            failures.Add($"{prefix}:ServerHostName must be ASCII and at most {MaxServerHostNameLength} characters.");
        }

        if (failures.Count > failuresBefore || method is null)
        {
            return null;
        }

        return new BootTarget
        {
            Architecture = architecture,
            Method = method.Value,
            BootFile = bootFile,
            ServerAddress = serverAddress,
            ServerHostName = target.ServerHostName,
            AdvertiseBootServerDiscovery = target.AdvertiseBootServerDiscovery,
        };
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // IPAddress.TryParse accepts "10" as 0.0.0.10, reads "010" as octal and accepts IPv6. Only an address
    // that round trips to exactly what was written is plain dotted decimal.
    private static IPAddress? TryParseVersion4(string value) =>
        IPAddress.TryParse(value, out IPAddress? address)
        && address.AddressFamily == AddressFamily.InterNetwork
        && string.Equals(address.ToString(), value, StringComparison.Ordinal)
            ? address
            : null;
}
