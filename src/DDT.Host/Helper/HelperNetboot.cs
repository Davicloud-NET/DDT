// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DDT.Core.Windows;
using DDT.Pxe;
using DDT.Server.BootImage;

namespace DDT.Host.Helper;

// What the helper does to this computer's DHCP server and WDS for the web server: it lists the DHCP scopes, sets
// their options 66 and 67, stops WDS or starts it again, and puts DDT's boot image into the WDS boot menu.
public sealed partial class HelperNetboot(HelperPaths paths, Func<ProcessStartInfo, Action<string>, CancellationToken, Task<int>> run)
{
    private const int MaxScopes = 256;
    private const int MaxHostNameLength = 253;

    public HelperNetboot(HelperPaths paths)
        : this(paths, ScriptProcess.RunAsync)
    {
    }

    public static bool Handles(string kind) =>
        kind is HelperRequest.Services or HelperRequest.DhcpScopes or HelperRequest.DhcpOptions or HelperRequest.WdsReplace or HelperRequest.WdsRestore
            or HelperRequest.WdsBootImage or HelperRequest.WdsRefresh;

    // Returns null when PowerShell ended well, or what stopped it.
    public async Task<string?> RunAsync(HelperRequest request, Action<string> line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(line);

        Dictionary<string, string> values = [];
        string script;

        switch (request.Kind)
        {
            case HelperRequest.Services:
                line(JsonSerializer.Serialize(new HelperServices(State("DHCPServer"), State("WDSServer")), HelperJsonContext.Default.HelperServices));

                return null;
            case HelperRequest.DhcpScopes:
                script = NetbootScripts.DhcpScopes;
                break;
            case HelperRequest.DhcpOptions:
                if (OptionsProblem(request) is { } problem)
                {
                    return problem;
                }

                script = NetbootScripts.DhcpOptions;
                values["DDT_SCOPES"] = string.Join(',', request.Scopes);
                values["DDT_BOOT_SERVER"] = request.BootServer ?? string.Empty;
                values["DDT_BOOT_FILE"] = request.BootFile ?? string.Empty;
                break;
            case HelperRequest.WdsReplace:
                script = NetbootScripts.WdsReplace;
                break;
            case HelperRequest.WdsRestore:
                script = NetbootScripts.WdsRestore;
                break;
            case HelperRequest.WdsBootImage or HelperRequest.WdsRefresh:
                // The build that is served, which the helper finds itself
                string image = Path.Combine(BootBuilds.Serving(paths.BootDirectory), "Boot", "boot.wim");

                if (!File.Exists(image))
                {
                    return request.Kind == HelperRequest.WdsRefresh ? null : "The server has no boot image yet. Build one first.";
                }

                script = NetbootScripts.WdsBootImage;
                values["DDT_WIM"] = image;
                values["DDT_IMAGE_NAME"] = NetbootScripts.ImageName;
                values["DDT_ONLY_REPLACE"] = request.Kind == HelperRequest.WdsRefresh ? "1" : "0";
                break;
            default:
                return $"The helper does nothing called {request.Kind}.";
        }

        return await PowerShellAsync(script, values, line, cancellationToken).ConfigureAwait(false);
    }

    private static HelperServiceState State(string service)
    {
        (bool installed, bool running, int process) = WindowsServices.State(service);

        return new HelperServiceState(installed, running, process);
    }

    private async Task<string?> PowerShellAsync(string script, Dictionary<string, string> values, Action<string> line, CancellationToken cancellationToken)
    {
        // Encoded, so nothing of the script meets a command line's quoting. An error ends it with its own words as the
        // last line, where PowerShell would write a record of it as XML.
        string guarded = "try {\n" + script + "\n} catch { Write-Output $_.Exception.Message; exit 1 }";
        ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            ArgumentList =
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-OutputFormat", "Text",
                "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(guarded)),
            },
        };

        foreach ((string name, string value) in values)
        {
            start.Environment[name] = value;
        }

        List<string> said = [];
        int exitCode = await run(
            start,
            text =>
            {
                said.Add(text);
                line(text);
            },
            cancellationToken).ConfigureAwait(false);

        // PowerShell's own words say more than its exit code
        return exitCode == 0 ? null : said.LastOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? $"PowerShell ended with exit code {exitCode}.";
    }

    // The web server faces the network, so the helper checks again what reaches the DHCP server.
    private static string? OptionsProblem(HelperRequest request)
    {
        if (request.Scopes.Count is 0 or > MaxScopes
            || !request.Scopes.All(scope => IPAddress.TryParse(scope, out IPAddress? address) && address.AddressFamily == AddressFamily.InterNetwork && address.ToString() == scope))
        {
            return "The request names no DHCP scopes by their addresses.";
        }

        if (request.BootServer is not { Length: > 0 and <= MaxHostNameLength } server
            || Uri.CheckHostName(server) == UriHostNameType.Unknown
            || !HostName().IsMatch(server))
        {
            return "The request names no server for option 66.";
        }

        return request.BootFile is { } file && BootFile().IsMatch(file) && !file.Contains("..", StringComparison.Ordinal)
            ? null
            : "The request names no boot file for option 67.";
    }

    [GeneratedRegex("^[A-Za-z0-9.-]+$")]
    private static partial Regex HostName();

    [GeneratedRegex(@"^[A-Za-z0-9_./\\-]{1,128}$")]
    private static partial Regex BootFile();
}
