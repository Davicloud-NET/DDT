// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.NetworkInformation;
using System.Net.Sockets;
using DDT.Agent.Deployment;
using DDT.Agent.Facts;

namespace DDT.Agent;

public sealed class HardwareMachineIdentityReader(IFirmwareTables firmware, ISystemHardware hardware, IUefiVariables uefi) : IMachineIdentityReader
{
    private readonly MachineFactsReader _facts = new(firmware, hardware, uefi);

    public HardwareMachineIdentityReader()
        : this(new FirmwareTables(), new SystemHardware(), new UefiVariables())
    {
    }

    public MachineIdentity Read()
    {
        SmbiosSystemInformation? system = ReadSmbios();
        (string primary, List<string> macs, List<string> addresses, NetworkFacts? network) = ReadMacAddresses();

        return new MachineIdentity(
            (system?.Uuid ?? Guid.Empty).ToString("D"),
            primary,
            macs,
            system?.Manufacturer,
            system?.ProductName,
            system?.SerialNumber,
            SecureBootState.Read(),
            SecureBootTrust.Read(),
            system?.ChassisType,
            addresses,
            _facts.Read(system, network));
    }

    private SmbiosSystemInformation? ReadSmbios() =>
        firmware.Read(FirmwareTables.RawSmbiosProvider, 0) is { } raw ? SmbiosParser.TryReadSystemInformation(raw) : null;

    // The primary MAC is the adapter that carries the default route, which is the one the server sees
    // and the one PXE booted from. Adapters without link still count, because they are part of the
    // machine a technician will recognise. The primary adapter's IPv4 addresses come along for the console, and its
    // settings for the facts.
    private static (string Primary, List<string> All, List<string> PrimaryAddresses, NetworkFacts? Network) ReadMacAddresses()
    {
        List<(string Mac, bool Primary, List<string> Addresses, IPInterfaceProperties Properties)> adapters = [];

        foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
            {
                continue;
            }

            byte[] address = adapter.GetPhysicalAddress().GetAddressBytes();

            if (address.Length != 6 || address.All(value => value == 0))
            {
                continue;
            }

            IPInterfaceProperties properties = adapter.GetIPProperties();
            bool hasGateway = adapter.OperationalStatus == OperationalStatus.Up
                && properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork
                    && !gateway.Address.Equals(System.Net.IPAddress.Any));
            List<string> addresses =
            [
                .. properties.UnicastAddresses
                    .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(unicast => unicast.Address.ToString()),
            ];

            adapters.Add((Convert.ToHexString(address), hasGateway, addresses, properties));
        }

        List<string> sorted = [.. adapters.Select(adapter => adapter.Mac).Distinct().Order(StringComparer.Ordinal)];

        if (sorted.Count == 0)
        {
            throw new InvalidOperationException("No network adapter with a MAC address was found.");
        }

        string primary = adapters.FirstOrDefault(adapter => adapter.Primary).Mac ?? sorted[0];

        // The server accepts at most this many, and a host with many virtual adapters has more. The
        // primary is always kept.
        List<string> all = [primary, .. sorted.Where(mac => mac != primary).Take(AgentLimits.MaxMacAddresses - 1)];
        (_, _, List<string> primaryAddresses, IPInterfaceProperties primaryProperties) = adapters.First(adapter => adapter.Mac == primary);

        return (primary, all, primaryAddresses, ReadNetwork(primaryProperties));
    }

    // The facts go without the network's settings rather than the machine without an identity.
    private static NetworkFacts? ReadNetwork(IPInterfaceProperties properties)
    {
        try
        {
            return NetworkFacts.Read(properties);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
