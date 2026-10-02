// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using DDT.Contracts.Netboot;
using DDT.Core.Windows;
using DDT.Pxe;
using DDT.Server.BootImage;
using DDT.Server.Certificates;

namespace DDT.Server.Netboot;

// Who else answers netboot on this computer. An MDT shop's server runs WDS, and often Microsoft's DHCP server, which
// hold the ports DDT would answer on.
public sealed class NetbootNeighbourhood(IBootImageHelper helper)
{
    public const string DhcpService = "DHCPServer";
    public const string WdsService = "WDSServer";

    // DHCP and ProxyDHCP, TFTP, and the PXE boot server
    private static readonly int[] s_ports = [67, 69, 4011];

    // What a DHCP server says in option 66: the name DDT's certificate carries
    public static string BootServer => ServerNames.DnsName();

    public NetbootNeighbours Read()
    {
        (bool Installed, bool Running, int ProcessId) dhcp = WindowsServices.State(DhcpService);
        (bool Installed, bool Running, int ProcessId) wds = WindowsServices.State(WdsService);

        return new NetbootNeighbours(
            OperatingSystem.IsWindows() ? Ports(dhcp.ProcessId, wds.ProcessId) : null,
            new NetbootService(dhcp.Installed, dhcp.Running),
            new NetbootService(wds.Installed, wds.Running),
            helper.Available,
            BootServer,
            PxeSetup.DefaultBootFile);
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
