// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;
using DDT.Pxe;
using DDT.Server.BootImage;

namespace DDT.Host.Helper;

// What a build request may say. The web server parses uploads and faces the network, so the helper trusts none of
// its values: each one is checked here before it reaches a path or a command line.
public static partial class HelperRequestRules
{
    private const int MaxWindowSize = 64;
    private const int MaxDrivers = 256;
    private const int MaxNameLength = 256;

    // Returns null for a request the helper can build, or what is wrong with it.
    public static string? BuildProblem(HelperRequest request, HelperPaths paths)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(paths);

        if (request.Name is null || !BootBuilds.IsBuildName(request.Name))
        {
            return "The request names no build.";
        }

        if (Directory.Exists(BootBuilds.FolderOf(paths.BootDirectory, request.Name)))
        {
            return $"The boot directory has a build {request.Name} already.";
        }

        if (ServerUrl(request) is null)
        {
            return "The request names no https address for the server.";
        }

        if (request.KeyboardLayout is not null && !KeyboardLayout().IsMatch(request.KeyboardLayout))
        {
            return "The keyboard layout is not an identifier like 0407:00000407.";
        }

        if (request.TftpWindowSize is < 1 or > MaxWindowSize)
        {
            return $"The TFTP window has to be between 1 and {MaxWindowSize}.";
        }

        if (request.DriverSetHash is not null && !Sha256().IsMatch(request.DriverSetHash))
        {
            return "The hash of the driver set is not a SHA-256.";
        }

        return request.Drivers.Count > MaxDrivers
            || request.Drivers.Any(driver => !Sha256().IsMatch(driver.Sha256) || driver.Name.Length > MaxNameLength || driver.Name.Any(char.IsControl))
            ? "The request names a driver package that is none."
            : null;
    }

    // Scheme, host and port again from what parsed, so nothing else of the string reaches the command line.
    public static string? ServerUrl(HelperRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Uri.TryCreate(request.ServerUrl, UriKind.Absolute, out Uri? address)
            && address.Scheme == Uri.UriSchemeHttps
            && address.UserInfo.Length == 0
            && address.AbsolutePath == "/"
            && address.Query.Length == 0
            && address.Fragment.Length == 0
            && Uri.CheckHostName(address.Host) != UriHostNameType.Unknown
                ? $"https://{address.Host}:{address.Port}"
                : null;
    }

    [GeneratedRegex("^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}$")]
    private static partial Regex KeyboardLayout();

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex Sha256();
}
