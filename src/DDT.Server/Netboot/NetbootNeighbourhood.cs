// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Text.Json;
using DDT.Contracts.Netboot;
using DDT.Core.Windows;
using DDT.Pxe;
using DDT.Server.BootImage;
using DDT.Server.Certificates;

namespace DDT.Server.Netboot;

// Who else answers netboot on this computer. An MDT shop's server runs WDS, and often Microsoft's DHCP server, which
// hold the ports DDT would answer on.
public sealed class NetbootNeighbourhood(NetbootHelper helper)
{
    public const string DhcpService = "DHCPServer";
    public const string WdsService = "WDSServer";

    // DHCP and ProxyDHCP, TFTP, and the PXE boot server
    private static readonly int[] s_ports = [67, 69, 4011];

    // What a DHCP server says in option 66: the name DDT's certificate carries
    public static string BootServer => ServerNames.DnsName();

    public async Task<NetbootNeighbours> ReadAsync(CancellationToken cancellationToken)
    {
        HelperServices services = await ServicesAsync(cancellationToken).ConfigureAwait(false);

        return new NetbootNeighbours(
            OperatingSystem.IsWindows() ? Ports(services.Dhcp.ProcessId, services.Wds.ProcessId) : null,
            new NetbootService(services.Dhcp.Installed, services.Dhcp.Running),
            new NetbootService(services.Wds.Installed, services.Wds.Running),
            helper.Available,
            BootServer,
            PxeSetup.DefaultBootFile);
    }

    // Windows hides a service such as the DHCP server from the web server's account, so the helper is asked where
    // there is one. Without it, this account's own look has to do.
    private async Task<HelperServices> ServicesAsync(CancellationToken cancellationToken)
    {
        if (helper.Available)
        {
            (IReadOnlyList<string> lines, string? problem) = await helper.RunAsync(new HelperRequest { Kind = HelperRequest.Services }, cancellationToken).ConfigureAwait(false);

            try
            {
                if (problem is null && lines.Count > 0 && JsonSerializer.Deserialize(lines[^1], HelperJsonContext.Default.HelperServices) is { } seen)
                {
                    return seen;
                }
            }
            catch (JsonException)
            {
                // An answer that is none: look from here instead
            }
        }

        return new HelperServices(Own(DhcpService), Own(WdsService));
    }

    private static HelperServiceState Own(string service)
    {
        (bool installed, bool running, int process) = WindowsServices.State(service);

        return new HelperServiceState(installed, running, process);
    }

    private static List<NetbootPort> Ports(int dhcp, int wds)
    {
        IReadOnlyDictionary<int, IReadOnlyList<int>> owners = UdpPortOwners.Of(s_ports);

        return
        [
            .. s_ports.Select(port => new NetbootPort(
                port,
                [.. owners.GetValueOrDefault(port, []).Select(process => new NetbootPortOwner(process, Name(process), Service(process, dhcp, wds)))])),
        ];
    }

    // Both services live in a svchost of their own, so the process id tells them apart where the name does not.
    private static string? Service(int process, int dhcp, int wds) =>
        process == Environment.ProcessId ? "DDT"
        : process != 0 && process == dhcp ? "DHCP"
        : process != 0 && process == wds ? "WDS"
        : null;

    private static string Name(int process)
    {
        try
        {
            using Process found = Process.GetProcessById(process);

            return found.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // It ended since the table was read
            return string.Empty;
        }
    }
}
