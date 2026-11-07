// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DDT.Contracts.Messages;
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
    // The boot manager signed by the Microsoft 2011 CA, which a boot image build puts there
    public const string DefaultBootFile = "x64/bootmgfw.efi";

    // The shim of the Linux boot environment, which BootFiles serves from the server's bundle
    public const string LinuxBootFile = BootFiles.LinuxFolder + "/" + BootFiles.Shim;

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

    // The same answers with the Linux boot environment's shim for 64-bit UEFI machines. Null without TFTP, which
    // serves the shim.
    public ProxyDhcpConfiguration? LinuxProxyDhcp { get; private init; }

    public DefaultBootEnvironment DefaultEnvironment { get; private init; }

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
            return [new("BootDirectory", ServerMessages.SettingsPxeBootDirectoryEmpty.With())];
        }

        string boot = BootDirectoryIn(options.BootDirectory, storePath);
        string store = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storePath));
        string keys = Path.Combine(store, "keys");
        string suggested = Path.Combine(store, "boot");

        if (string.Equals(boot, Path.GetPathRoot(boot), StringComparison.OrdinalIgnoreCase))
        {
            return [new("BootDirectory", ServerMessages.SettingsPxeBootDirectoryIsRoot.With("directory", boot, "suggested", suggested))];
        }

        List<SettingProblem> problems = [];

        if (IsSameOrInside(store, boot))
        {
            problems.Add(new(
                "BootDirectory",
                ServerMessages.SettingsPxeBootDirectoryHoldsStore.With("directory", boot, "store", store, "suggested", suggested)));
        }
        else if (IsSameOrInside(boot, keys))
        {
            problems.Add(new(
                "BootDirectory",
                ServerMessages.SettingsPxeBootDirectoryInKeys.With("directory", boot, "keys", keys, "suggested", suggested)));
        }

        foreach (string file in CertificateFilesIn(configuration))
        {
            if (Path.GetDirectoryName(Path.GetFullPath(file)) is { } folder
                && IsSameOrInside(Path.TrimEndingDirectorySeparator(folder), boot))
            {
                problems.Add(new(
                    "BootDirectory",
                    ServerMessages.SettingsPxeBootDirectoryHoldsKey.With("directory", boot, "file", file, "suggested", suggested)));
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
            TftpLimits.Default with { MaxWindowSize = options.TftpMaxWindowSize })
        {
            LinuxProxyDhcp = options.EnableTftp ? proxyDhcp with { BootTargets = WithLinux(targets) } : null,
            DefaultEnvironment = ReadDefaultEnvironment(options.DefaultBootEnvironment) ?? DefaultBootEnvironment.Auto,
        };
    }

    // Matched against the member names rather than Enum.TryParse, which also accepts "1". Null for no such name.
    public static DefaultBootEnvironment? ReadDefaultEnvironment(string? value) =>
        Enum.GetValues<DefaultBootEnvironment>()
            .Cast<DefaultBootEnvironment?>()
            .FirstOrDefault(member => string.Equals(member.ToString(), value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static FrozenDictionary<ClientArchitecture, BootTarget> WithLinux(Dictionary<ClientArchitecture, BootTarget> targets) =>
        new Dictionary<ClientArchitecture, BootTarget>(targets)
        {
            [ClientArchitecture.X64Uefi] = new BootTarget
            {
                Architecture = ClientArchitecture.X64Uefi,
                Method = BootMethod.Tftp,
                BootFile = LinuxBootFile,
            },
        }.ToFrozenDictionary();

    private static (Dictionary<ClientArchitecture, BootTarget> Targets, ImmutableArray<IPAddress> Relays) Read(
        PxeOptions options,
        List<SettingProblem> problems)
    {
        Dictionary<ClientArchitecture, BootTarget> targets = BootTargetReader.Read(options, problems);
        ImmutableArray<IPAddress>.Builder relays = ImmutableArray.CreateBuilder<IPAddress>();

        foreach (string relay in options.AuthorisedRelayAgents.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (BootTargetReader.TryParseVersion4(relay) is { } address)
            {
                relays.Add(address);
            }
            else
            {
                problems.Add(new("AuthorisedRelayAgents", ServerMessages.SettingsPxeNotIpv4Address.With("value", relay)));
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
                    problems.Add(new($"BootTargets:{architecture}:ServerAddress", ServerMessages.SettingsPxeServerAddressRequired.With()));
                }
            }
        }

        if (options.HttpBootPort is < 1 or > 65535)
        {
            problems.Add(new("HttpBootPort", ServerMessages.SettingsPxeHttpBootPortInvalid.With("port", options.HttpBootPort)));
        }

        if (options.TftpMaxWindowSize is < 1 or > MaxWindowSize)
        {
            problems.Add(new("TftpMaxWindowSize", ServerMessages.SettingsPxeWindowSizeRange.With("max", MaxWindowSize)));
        }

        if (options.MaxConcurrentTftpTransfers < 1)
        {
            problems.Add(new("MaxConcurrentTftpTransfers", ServerMessages.SettingsAtLeastOne.With()));
        }

        if (ReadDefaultEnvironment(options.DefaultBootEnvironment) is null)
        {
            problems.Add(new("DefaultBootEnvironment", ServerMessages.SettingsPxeBootEnvironmentInvalid.With("value", options.DefaultBootEnvironment ?? string.Empty)));
        }

        if (KernelArgumentsProblem(options.LinuxKernelArguments) is { } problem)
        {
            problems.Add(new("LinuxKernelArguments", problem));
        }

        return (targets, relays.ToImmutable());
    }

    // The text ends up in the script GRUB runs on every machine, so each word is checked, and the first bad one named.
    private static ServerMessage? KernelArgumentsProblem(string? text)
    {
        if ((text?.Length ?? 0) > KernelArguments.MaxLength)
        {
            return ServerMessages.SettingsPxeKernelArgumentsLength.With("max", KernelArguments.MaxLength);
        }

        foreach (string word in KernelArguments.Split(text))
        {
            if (!KernelArguments.IsWord(word))
            {
                return ServerMessages.SettingsPxeKernelArgumentsInvalid.With("word", word);
            }

            if (KernelArguments.IsReserved(word))
            {
                return ServerMessages.SettingsPxeKernelArgumentsReserved.With("word", word);
            }
        }

        return null;
    }

    // Kestrel loads a PFX from Path alone, and a PEM key sits next to its certificate, so every certificate file
    // counts. That includes the ones under Kestrel:Certificates, and under each endpoint and its SNI entries.
    private static IEnumerable<string> CertificateFilesIn(IConfiguration configuration) =>
        configuration.GetSection("Kestrel").AsEnumerable()
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Value)
                && (setting.Key.EndsWith(":Path", StringComparison.OrdinalIgnoreCase)
                    || setting.Key.EndsWith(":KeyPath", StringComparison.OrdinalIgnoreCase)))
            .Select(setting => setting.Value!);

    // Ignores case. On a case sensitive filesystem, that refuses slightly more than it has to.
    private static bool IsSameOrInside(string path, string directory) =>
        string.Equals(path, directory, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(
            Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
