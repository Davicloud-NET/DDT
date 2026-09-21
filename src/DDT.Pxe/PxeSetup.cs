// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DDT.Core.Configuration;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;
using DDT.Protocols.Tftp;
using Microsoft.Extensions.Configuration;

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

    public static IReadOnlyList<SettingProblem> FindProblems(PxeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<SettingProblem> problems = [];
        _ = Read(options, problems);

        return problems;
    }

    public static string BootDirectoryIn(string bootDirectory, string storePath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(storePath, bootDirectory)));

    // Everything below the boot directory is served without a credential, over TFTP and plain HTTP, so it must not
    // hold the store with its database, the key ring that signs administrator cookies, or a TLS key.
    public static IReadOnlyList<SettingProblem> FindBootDirectoryProblems(PxeOptions options, string storePath, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        ArgumentNullException.ThrowIfNull(configuration);

        // Resolved against the store, an empty value would be the store itself.
        if (string.IsNullOrWhiteSpace(options.BootDirectory))
        {
            return [new("BootDirectory", "Must not be empty. Leave it out for the folder boot in DDT:StorePath.")];
        }

        string boot = BootDirectoryIn(options.BootDirectory, storePath);
        string store = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storePath));
        string keys = Path.Combine(store, "keys");
        string suggestion = $"Use a directory of its own, such as {Path.Combine(store, "boot")}.";

        if (string.Equals(boot, Path.GetPathRoot(boot), StringComparison.OrdinalIgnoreCase))
        {
            return [new("BootDirectory", $"'{boot}' is the root of a filesystem, which would serve every file on it. {suggestion}")];
        }

        List<SettingProblem> problems = [];

        if (IsSameOrInside(store, boot))
        {
            problems.Add(new(
                "BootDirectory",
                $"'{boot}' holds DDT:StorePath, {store}, which would serve the database and the key ring. {suggestion}"));
        }
        else if (IsSameOrInside(boot, keys))
        {
            problems.Add(new("BootDirectory", $"'{boot}' is in the key ring folder {keys}, which would serve its keys. {suggestion}"));
        }

        foreach (string file in CertificateFilesIn(configuration))
        {
            if (Path.GetDirectoryName(Path.GetFullPath(file)) is { } folder
                && IsSameOrInside(Path.TrimEndingDirectorySeparator(folder), boot))
            {
                problems.Add(new(
                    "BootDirectory",
                    $"'{boot}' holds the folder of the TLS certificate or key {file}, which would serve the key. {suggestion}"));
            }
        }

        return problems;
    }

    public static PxeSetup Create(PxeOptions options, NetworkInterfaceMap interfaces)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(interfaces);

        List<SettingProblem> problems = [];
        (Dictionary<ClientArchitecture, BootTarget> targets, ImmutableArray<IPAddress> relays) = Read(options, problems);
        SettingProblem.ThrowIfAny(PxeOptions.SectionName, problems);

        ProxyDhcpConfiguration proxyDhcp = new()
        {
            BootTargets = targets.ToFrozenDictionary(),
            LocalAddresses = [.. interfaces.Served.SelectMany(served => served.Addresses)],
            AuthorisedRelayAgents = relays,
        };

        return new PxeSetup(
            options,
            interfaces,
            new BootFileResolver(options.BootDirectory),
            proxyDhcp,
            TftpLimits.Default with { MaxWindowSize = options.TftpMaxWindowSize });
    }

    private static (Dictionary<ClientArchitecture, BootTarget> Targets, ImmutableArray<IPAddress> Relays) Read(
        PxeOptions options,
        List<SettingProblem> problems)
    {
        Dictionary<ClientArchitecture, BootTarget> targets = [];

        foreach ((string key, BootTargetOptions target) in options.BootTargets)
        {
            if (TryReadTarget(key, target, problems) is { } parsed)
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
                problems.Add(new("AuthorisedRelayAgents", $"'{relay}' is not an IPv4 address."));
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
                    problems.Add(new(
                        $"BootTargets:{architecture}:ServerAddress",
                        "Required while EnableTftp is false, because this target uses Tftp. Name the TFTP server that serves it."));
                }
            }
        }

        if (options.HttpBootPort is < 1 or > 65535)
        {
            problems.Add(new("HttpBootPort", $"{options.HttpBootPort} is not a port number."));
        }

        if (options.TftpMaxWindowSize is < 1 or > MaxWindowSize)
        {
            problems.Add(new("TftpMaxWindowSize", $"Must be between 1 and {MaxWindowSize}."));
        }

        if (options.MaxConcurrentTftpTransfers < 1)
        {
            problems.Add(new("MaxConcurrentTftpTransfers", "Must be at least 1."));
        }

        return (targets, relays.ToImmutable());
    }

    private static BootTarget? TryReadTarget(string key, BootTargetOptions target, List<SettingProblem> problems)
    {
        string field = "BootTargets:" + key;

        // Matched against the member names rather than Enum.TryParse, which also accepts "7".
        string? name = Enum.GetNames<ClientArchitecture>()
            .FirstOrDefault(candidate => candidate.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (name is null)
        {
            problems.Add(new(
                field,
                $"'{key}' is not a client architecture. Use one of: {string.Join(", ", Enum.GetNames<ClientArchitecture>())}."));

            return null;
        }

        ClientArchitecture architecture = Enum.Parse<ClientArchitecture>(name);
        int problemsBefore = problems.Count;

        BootMethod? method = target.Method?.Trim().ToUpperInvariant() switch
        {
            "TFTP" => BootMethod.Tftp,
            "HTTP" => BootMethod.Http,
            _ => null,
        };

        if (method is null)
        {
            problems.Add(new($"{field}:Method", "Must be Tftp or Http."));
        }

        // The architecture already says which one the firmware speaks: a PXE client never accepts a
        // URL and an HTTP Boot client never accepts a TFTP path.
        bool httpArchitecture = name.EndsWith("Http", StringComparison.Ordinal);

        if (method is { } chosen && httpArchitecture != (chosen == BootMethod.Http))
        {
            problems.Add(new($"{field}:Method", $"Must be {(httpArchitecture ? "Http" : "Tftp")} for {name} clients."));
        }

        string bootFile = target.BootFile?.Trim() ?? string.Empty;

        if (bootFile.Length == 0)
        {
            problems.Add(new($"{field}:BootFile", "Must be set."));
        }
        else if (bootFile.Length > MaxBootFileLength || !Ascii.IsValid(bootFile))
        {
            problems.Add(new($"{field}:BootFile", $"Must be ASCII and at most {MaxBootFileLength} characters."));
        }
        else if (method == BootMethod.Http && !IsHttpUrl(bootFile))
        {
            problems.Add(new($"{field}:BootFile", "Must be an absolute http or https URL."));
        }

        IPAddress? serverAddress = null;

        if (!string.IsNullOrWhiteSpace(target.ServerAddress))
        {
            serverAddress = TryParseVersion4(target.ServerAddress.Trim());

            if (serverAddress is null)
            {
                problems.Add(new($"{field}:ServerAddress", $"'{target.ServerAddress}' is not an IPv4 address."));
            }
        }

        if (target.ServerHostName is { } hostName
            && (hostName.Length > MaxServerHostNameLength || !Ascii.IsValid(hostName)))
        {
            problems.Add(new($"{field}:ServerHostName", $"Must be ASCII and at most {MaxServerHostNameLength} characters."));
        }

        if (problems.Count > problemsBefore || method is null)
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

    // Kestrel loads a PFX from Path alone, and a PEM key is kept next to its certificate, so every certificate file
    // counts: under Kestrel:Certificates, and under each endpoint and its SNI entries.
    private static IEnumerable<string> CertificateFilesIn(IConfiguration configuration) =>
        configuration.GetSection("Kestrel").AsEnumerable()
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Value)
                && (setting.Key.EndsWith(":Path", StringComparison.OrdinalIgnoreCase)
                    || setting.Key.EndsWith(":KeyPath", StringComparison.OrdinalIgnoreCase)))
            .Select(setting => setting.Value!);

    // Without regard to case, which on a case sensitive filesystem refuses slightly more than it has to.
    private static bool IsSameOrInside(string path, string directory) =>
        string.Equals(path, directory, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(
            Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

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
