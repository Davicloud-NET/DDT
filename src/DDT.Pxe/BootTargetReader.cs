// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using System.Text;
using DDT.Contracts.Messages;
using DDT.Core.Configuration;
using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;

namespace DDT.Pxe;

// Reads the boot targets of DDT:Pxe, and names every value that would send a machine to a file it cannot load.
internal static class BootTargetReader
{
    private const int MaxServerHostNameLength = 63;
    // Option 67 holds 255 bytes, one of them the NUL.
    private const int MaxBootFileLength = 254;

    public static Dictionary<ClientArchitecture, BootTarget> Read(PxeOptions options, List<SettingProblem> problems)
    {
        Dictionary<ClientArchitecture, BootTarget> targets = [];

        foreach ((string key, BootTargetOptions target) in options.BootTargets)
        {
            if (TryReadTarget(key, target, problems) is { } parsed)
            {
                targets[parsed.Architecture] = parsed;
            }
        }

        // A new server boots machines without a visit to the settings. Without TFTP, nothing here would serve the file.
        if (options.BootTargets.Count == 0 && options.EnableProxyDhcp && options.EnableTftp)
        {
            targets[ClientArchitecture.X64Uefi] = new BootTarget
            {
                Architecture = ClientArchitecture.X64Uefi,
                Method = BootMethod.Tftp,
                BootFile = PxeSetup.DefaultBootFile,
            };
        }

        return targets;
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
                ServerMessages.SettingsPxeArchitectureUnknown.With("value", key, "architectures", string.Join(", ", Enum.GetNames<ClientArchitecture>()))));

            return null;
        }

        ClientArchitecture architecture = Enum.Parse<ClientArchitecture>(name);
        int problemsBefore = problems.Count;
        BootMethod? method = ReadMethod(name, target.Method, field, problems);
        string bootFile = ReadBootFile(target.BootFile, method, field, problems);
        IPAddress? serverAddress = ReadServerAddress(target.ServerAddress, field, problems);

        if (target.ServerHostName is { } hostName
            && (hostName.Length > MaxServerHostNameLength || !Ascii.IsValid(hostName)))
        {
            problems.Add(new($"{field}:ServerHostName", ServerMessages.SettingsPxeAsciiMaxLength.With("max", MaxServerHostNameLength)));
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

    private static BootMethod? ReadMethod(string architecture, string? value, string field, List<SettingProblem> problems)
    {
        BootMethod? method = value?.Trim().ToUpperInvariant() switch
        {
            "TFTP" => BootMethod.Tftp,
            "HTTP" => BootMethod.Http,
            _ => null,
        };

        if (method is null)
        {
            problems.Add(new($"{field}:Method", ServerMessages.SettingsPxeMethodInvalid.With()));
        }

        // The architecture already says which one the firmware speaks. A PXE client never accepts a URL, and an HTTP
        // Boot client never accepts a TFTP path.
        bool httpArchitecture = architecture.EndsWith("Http", StringComparison.Ordinal);

        if (method is { } chosen && httpArchitecture != (chosen == BootMethod.Http))
        {
            problems.Add(new(
                $"{field}:Method",
                ServerMessages.SettingsPxeMethodForArchitecture.With("method", httpArchitecture ? "Http" : "Tftp", "architecture", architecture)));
        }

        return method;
    }

    private static string ReadBootFile(string? value, BootMethod? method, string field, List<SettingProblem> problems)
    {
        string bootFile = value?.Trim() ?? string.Empty;

        if (bootFile.Length == 0)
        {
            problems.Add(new($"{field}:BootFile", ServerMessages.SettingsPxeBootFileRequired.With()));
        }
        else if (bootFile.Length > MaxBootFileLength || !Ascii.IsValid(bootFile))
        {
            problems.Add(new($"{field}:BootFile", ServerMessages.SettingsPxeAsciiMaxLength.With("max", MaxBootFileLength)));
        }
        else if (method == BootMethod.Http && !IsHttpUrl(bootFile))
        {
            problems.Add(new($"{field}:BootFile", ServerMessages.SettingsPxeBootFileUrl.With()));
        }

        return bootFile;
    }

    private static IPAddress? ReadServerAddress(string? value, string field, List<SettingProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        IPAddress? serverAddress = TryParseVersion4(value.Trim());

        if (serverAddress is null)
        {
            problems.Add(new($"{field}:ServerAddress", ServerMessages.SettingsPxeNotIpv4Address.With("value", value)));
        }

        return serverAddress;
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // IPAddress.TryParse accepts "10" as 0.0.0.10, reads "010" as octal and accepts IPv6. Only an address
    // that round trips to exactly what was written is plain dotted decimal.
    public static IPAddress? TryParseVersion4(string value) =>
        IPAddress.TryParse(value, out IPAddress? address)
        && address.AddressFamily == AddressFamily.InterNetwork
        && string.Equals(address.ToString(), value, StringComparison.Ordinal)
            ? address
            : null;
}
